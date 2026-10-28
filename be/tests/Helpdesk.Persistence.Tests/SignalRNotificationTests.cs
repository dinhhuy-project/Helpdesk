using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using be.Domain.Constants;
using be.Infrastructure.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static Helpdesk.Persistence.Tests.NotificationTestContext;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class SignalRNotificationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task HubRequiresValidJwtAndQueryTokensCannotAuthenticateRestEndpoints()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, realSender: true);
        using var anonymous = test.Factory.CreateClient();
        using var unauthorized = await anonymous.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        var token = admin.DefaultRequestHeaders.Authorization!.Parameter!;
        using var valid = await anonymous.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1&access_token=" + Uri.EscapeDataString(token), null);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var rest = await anonymous.GetAsync("/api/v1/notifications?access_token=" + Uri.EscapeDataString(token));
        Assert.Equal(HttpStatusCode.Unauthorized, rest.StatusCode);
        using var invalid = await anonymous.PostAsync("/hubs/notifications/negotiate?access_token=invalid-token", null);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.DoesNotContain("invalid-token", await invalid.Content.ReadAsStringAsync());

        var original = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var expired = new JwtSecurityToken(AuthTestSettings.Issuer, AuthTestSettings.Audience,
            original.Claims.Where(x => x.Type is not "exp" and not "nbf"), DateTime.UtcNow.AddMinutes(-20), DateTime.UtcNow.AddMinutes(-10),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTestSettings.SigningKey)), SecurityAlgorithms.HmacSha256));
        expired.Header["typ"] = "at+jwt";
        using var expiredResponse = await anonymous.PostAsync("/hubs/notifications/negotiate?access_token=" +
            Uri.EscapeDataString(new JwtSecurityTokenHandler().WriteToken(expired)), null);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
        await using var database = test.Data.Accounts.Database();
        var actor = Actor(admin);
        var revokedAt = DateTime.UtcNow;
        await database.RefreshTokens.Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, revokedAt));
        using var revoked = await anonymous.PostAsync("/hubs/notifications/negotiate?access_token=" + Uri.EscapeDataString(token), null);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [PostgresFact]
    public async Task RealSignalRDeliversOnlyAfterDispatchAndAllowsOnlyAuthorizedTicketGroups()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, realSender: true);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        var outside = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        using var client = await test.ClientAsync(owner);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        await using var connection = CreateConnection(test, client);
        var notification = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("NotificationChanged", value => notification.TrySetResult(value));
        connection.On<JsonElement>("TicketChanged", value => changed.TrySetResult(value));
        await connection.StartAsync();
        await connection.InvokeAsync("WatchTicket", ticket.Id);
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("WatchTicket", outside.Id));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinGroup", "user:" + test.Data.Accounts.AdminUser.Id));
        using var response = await CommentAsync(admin, ticket.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(notification.Task.IsCompleted);
        Assert.False(changed.Task.IsCompleted);
        await test.DispatchAsync();
        var signal = await notification.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ticketSignal = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(signal.EnumerateObject());
        Assert.Equal(ticket.Id, ticketSignal.GetProperty("id").GetGuid());
        await using var database = test.Data.Accounts.Database();
        Assert.True(await database.Notifications.AnyAsync(x => x.Id == signal.GetProperty("id").GetGuid() && x.UserId == owner.Id));
        Assert.DoesNotContain("private-comment-marker", signal.GetRawText());
        await connection.InvokeAsync("UnwatchTicket", ticket.Id);
        Assert.Empty(test.Connections.Find(connection.ConnectionId!)!.Tickets);
        await connection.StopAsync();
    }

    [PostgresFact]
    public async Task GroupMembershipIsRemovedAfterResourceScopeChangesAndSessionRevocationClosesConnection()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, realSender: true);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, test.Data.TeamA);
        using var client = await test.ClientAsync(agent);
        await using var connection = CreateConnection(test, client);
        await connection.StartAsync();
        await connection.InvokeAsync("WatchTicket", ticket.Id);
        var connectionId = connection.ConnectionId!;
        Assert.Contains(ticket.Id, test.Connections.Find(connectionId)!.Tickets);
        await using var database = test.Data.Accounts.Database();
        await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        await using (var scope = test.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<RealtimeConnectionValidator>().RunOnceAsync(CancellationToken.None);
        Assert.Empty(test.Connections.Find(connectionId)!.Tickets);
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("WatchTicket", ticket.Id));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        var actor = Actor(client);
        var revokedAt = DateTime.UtcNow;
        await database.RefreshTokens.Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, revokedAt));
        await using (var scope = test.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<RealtimeConnectionValidator>().RunOnceAsync(CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(test.Connections.Find(connectionId));
    }

    private static HubConnection CreateConnection(NotificationTestContext test, HttpClient client) => new HubConnectionBuilder()
        .WithUrl(new Uri(client.BaseAddress!, NotificationsHub.Path), options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.AccessTokenProvider = () => Task.FromResult(client.DefaultRequestHeaders.Authorization!.Parameter);
            options.HttpMessageHandlerFactory = _ => test.Factory.Server.CreateHandler();
        }).Build();
}
