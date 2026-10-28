using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Notifications;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using be.Infrastructure.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;
using static Helpdesk.Persistence.Tests.NotificationTestContext;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class NotificationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task NotificationPaginationAndUnreadFilterUseOnlyVisibleOwnedRows()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        using var client = await test.ClientAsync(owner);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        for (var i = 0; i < 3; i++)
        {
            using var comment = await CommentAsync(admin, ticket.Id);
            Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        }
        var first = (await client.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications?pageSize=1"))!;
        var second = (await client.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications?pageSize=1&page=2"))!;
        Assert.Equal(3, first.TotalCount);
        Assert.NotEqual(Assert.Single(first.Items).Id, Assert.Single(second.Items).Id);
        using var read = await client.PutAsync($"/api/v1/notifications/{first.Items[0].Id}/read", null);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications?unread=true"))!.TotalCount);
        Assert.Equal(first.Items[0].Id, Assert.Single((await client.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications?unread=false"))!.Items).Id);
        foreach (var query in new[] { "page=0", "page=1000001", "pageSize=101", "pageSize=0", "unread=invalid" })
        {
            using var invalid = await client.GetAsync("/api/v1/notifications?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("Dữ liệu đầu vào không hợp lệ.", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        }
    }

    [PostgresFact]
    public async Task CreationAssignmentAndStatusPersistNotificationsWithoutDuplicateNoOpCommands()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        using var requester = await test.ClientAsync(owner);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var processor = await test.ClientAsync(agent);
        using var created = await requester.PostAsJsonAsync("/api/v1/tickets", new
        { title = "Notification lifecycle", description = "Private description", categoryId = test.Data.Category });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<TicketCreated>())!;
        test.Data.OwnTicket(receipt.Id);
        await using var database = test.Data.Accounts.Database();
        var creation = await database.Notifications.SingleAsync(x => x.TicketId == receipt.Id && x.UserId == test.Data.Accounts.AdminUser.Id);
        Assert.Equal(TicketActivityActions.Created, (await database.TicketActivities.SingleAsync(x => x.Id == creation.TicketActivityId)).Action);
        Assert.False(await database.Notifications.AnyAsync(x => x.TicketId == receipt.Id && x.UserId == owner.Id));

        using var assigned = await admin.PostAsJsonAsync($"/api/v1/tickets/{receipt.Id}/assign", new
        { teamId = test.Data.TeamA, agentId = agent.Id, version = receipt.Version });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var assignment = (await assigned.Content.ReadFromJsonAsync<TicketMutationResult>())!;
        var assignedActivity = await database.TicketActivities.SingleAsync(x => x.TicketId == receipt.Id && x.Action == TicketActivityActions.Assigned);
        var recipients = await database.Notifications.Where(x => x.TicketActivityId == assignedActivity.Id).Select(x => x.UserId).ToArrayAsync();
        Assert.Contains(owner.Id, recipients);
        Assert.Contains(agent.Id, recipients);
        Assert.DoesNotContain(test.Data.Accounts.AdminUser.Id, recipients);
        using var repeat = await admin.PostAsJsonAsync($"/api/v1/tickets/{receipt.Id}/assign", new
        { teamId = test.Data.TeamA, agentId = agent.Id, version = assignment.Version });
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(recipients.Length, await database.Notifications.CountAsync(x => x.TicketActivityId == assignedActivity.Id));

        using var started = await processor.PostAsJsonAsync($"/api/v1/tickets/{receipt.Id}/transition", new
        { status = TicketStatus.InProgress, version = assignment.Version });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var stateActivity = await database.TicketActivities.SingleAsync(x => x.TicketId == receipt.Id && x.Action == TicketActivityActions.StatusChanged);
        Assert.True(await database.Notifications.AnyAsync(x => x.TicketActivityId == stateActivity.Id && x.UserId == owner.Id));
        Assert.False(await database.Notifications.AnyAsync(x => x.TicketActivityId == stateActivity.Id && x.UserId == agent.Id));
        Assert.Empty(test.Sender.Sent);
    }

    [PostgresFact]
    public async Task InternalPermissionRevocationHidesExistingNotificationsAndSuppressesTheirSignals()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var member = await test.Data.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketCommentInternal);
        await test.Data.MemberAsync(member.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(owner.Id, test.Data.TeamA);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var recipient = await test.ClientAsync(member);
        test.Connections.Add(new("internal-revoked", Actor(recipient), DateTime.UtcNow.AddMinutes(5), () => { }));
        using var note = await CommentAsync(admin, ticket.Id, true);
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        var notification = Assert.Single((await recipient.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications"))!.Items);
        await using var database = test.Data.Accounts.Database();
        var roles = database.UserRoles.Where(x => x.UserId == member.Id).Select(x => x.RoleId);
        var permission = await database.Permissions.SingleAsync(x => x.Code == PermissionCodes.TicketCommentInternal);
        await database.RolePermissions.Where(x => roles.Contains(x.RoleId) && x.PermissionId == permission.Id).ExecuteDeleteAsync();
        using var stillReadable = await recipient.GetAsync($"/api/v1/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.OK, stillReadable.StatusCode);
        using var hidden = await recipient.GetAsync($"/api/v1/notifications/{notification.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        await test.DispatchAsync();
        Assert.DoesNotContain(test.Sender.Sent, x => x.Connection == "internal-revoked");
    }

    [PostgresFact]
    public async Task PublicAndInternalRecipientsUseCurrentAclAndNeverExposeCommentBodies()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        var teammate = await test.Data.UserAsync(RoleNames.SupportAgent);
        var outsider = await test.Data.UserAsync(RoleNames.Requester);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        await test.Data.MemberAsync(teammate.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(owner.Id, test.Data.TeamA, agent.Id);
        using var agentClient = await test.ClientAsync(agent);
        using var ownerClient = await test.ClientAsync(owner);
        using var teammateClient = await test.ClientAsync(teammate);
        using var outsiderClient = await test.ClientAsync(outsider);
        using var publicComment = await CommentAsync(agentClient, ticket.Id);
        Assert.Equal(HttpStatusCode.Created, publicComment.StatusCode);
        using var internalComment = await CommentAsync(agentClient, ticket.Id, true);
        Assert.Equal(HttpStatusCode.Created, internalComment.StatusCode);
        var ownPage = (await ownerClient.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications"))!;
        var teamPage = (await teammateClient.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications"))!;
        Assert.Equal(1, ownPage.TotalCount);
        Assert.False(Assert.Single(ownPage.Items).IsInternal);
        Assert.Equal(2, teamPage.TotalCount);
        Assert.Single(teamPage.Items, item => item.IsInternal);
        Assert.Equal(0, (await agentClient.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications"))!.TotalCount);
        Assert.Equal(0, (await outsiderClient.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications?userId=" + owner.Id))!.TotalCount);
        using var guessed = await outsiderClient.GetAsync("/api/v1/notifications/" + ownPage.Items[0].Id);
        Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
        using var check = await teammateClient.GetAsync("/api/v1/notifications");
        var body = await check.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-comment-marker", body);
        Assert.DoesNotContain("objectKey", body);
        Assert.DoesNotContain("email", body);
        // A requester with multiple roles is still not an internal recipient.
        await test.Data.Accounts.AssignAsync(owner.Id, await test.Data.Accounts.RoleIdAsync(RoleNames.SystemAdmin));
        using var anotherInternal = await CommentAsync(agentClient, ticket.Id, true);
        Assert.Equal(1, (await ownerClient.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications"))!.TotalCount);
    }

    [PostgresFact]
    public async Task MarkReadIsOwnedIdempotentAndDisappearsAfterReadGrantRevocation()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.CustomUserAsync(PermissionCodes.TicketReadOwn);
        var outsider = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var client = await test.ClientAsync(owner);
        using var other = await test.ClientAsync(outsider);
        using var comment = await CommentAsync(admin, ticket.Id);
        var notification = Assert.Single((await client.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications"))!.Items);
        using var deny = await other.PutAsync($"/api/v1/notifications/{notification.Id}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, deny.StatusCode);
        using var first = await client.PutAsync($"/api/v1/notifications/{notification.Id}/read", null);
        var read = (await first.Content.ReadFromJsonAsync<NotificationDetails>())!;
        Assert.NotNull(read.ReadAt);
        using var second = await client.PutAsync($"/api/v1/notifications/{notification.Id}/read", null);
        Assert.Equal(read.ReadAt, (await second.Content.ReadFromJsonAsync<NotificationDetails>())!.ReadAt);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count")).GetProperty("count").GetInt32());
        using var invalid = await client.PutAsJsonAsync($"/api/v1/notifications/{notification.Id}/read", new { userId = outsider.Id });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await using var database = test.Data.Accounts.Database();
        var roleIds = database.UserRoles.Where(x => x.UserId == owner.Id).Select(x => x.RoleId);
        await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId)).ExecuteDeleteAsync();
        Assert.Equal(0, (await client.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications"))!.TotalCount);
        using var hidden = await client.PutAsync($"/api/v1/notifications/{notification.Id}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [PostgresTheory]
    [InlineData("membership")]
    [InlineData("session")]
    [InlineData("user")]
    [InlineData("expiry")]
    public async Task RevocationBeforeDispatchCannotSendPersistedNotifications(string change)
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, test.Data.TeamA);
        using var recipient = await test.ClientAsync(agent);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        var actor = Actor(recipient);
        var aborted = false;
        var connection = new RealtimeConnection("revoked", actor, change == "expiry" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5), () => aborted = true);
        connection.Watch(ticket.Id);
        test.Connections.Add(connection);
        using var comment = await CommentAsync(admin, ticket.Id);
        await using var database = test.Data.Accounts.Database();
        Assert.True(await database.Notifications.AnyAsync(x => x.UserId == agent.Id && x.TicketId == ticket.Id));
        if (change == "membership") await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        if (change == "session")
        {
            var revokedAt = DateTime.UtcNow;
            await database.RefreshTokens.Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, revokedAt));
        }
        if (change == "user") await database.Users.Where(x => x.Id == agent.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        await test.DispatchAsync();
        Assert.DoesNotContain(test.Sender.Sent, x => x.Connection == "revoked");
        if (change != "membership") Assert.True(aborted);
    }

    [PostgresFact]
    public async Task DurableDispatchRetriesAndDoesNotHoldTheMutationLockDuringSignalRIo()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var recipient = await test.ClientAsync(owner);
        var connection = new RealtimeConnection("retry", Actor(recipient), DateTime.UtcNow.AddMinutes(5), () => { });
        connection.Watch(ticket.Id);
        test.Connections.Add(connection);
        using var comment = await CommentAsync(admin, ticket.Id);
        var notification = Assert.Single((await recipient.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications"))!.Items);
        test.Sender.Fail = true;
        await test.DispatchAsync();
        await using (var database = test.Data.Accounts.Database())
        {
            Assert.Null((await database.Notifications.SingleAsync(x => x.Id == notification.Id)).DispatchedAt);
            var retryAt = DateTime.UtcNow.AddMinutes(-1);
            await database.Notifications.Where(x => x.Id == notification.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.DispatchLeaseUntil, retryAt));
        }
        test.Sender.Fail = false;
        test.Sender.BeforeSend = async () =>
        {
            await using var check = test.Data.Accounts.Database();
            await using var transaction = await check.Database.BeginTransactionAsync();
            Assert.True(await check.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(734205001) AS \"Value\"").SingleAsync());
            Assert.True(await check.Notifications.AnyAsync(x => x.Id == notification.Id));
        };
        await Task.WhenAll(test.DispatchAsync(), test.DispatchAsync());
        Assert.Single(test.Sender.Sent, x => x.Connection == "retry" && x.Event == "NotificationChanged" && x.Id == notification.Id);
        Assert.Single(test.Sender.Sent, x => x.Connection == "retry" && x.Event == "TicketChanged" && x.Id == ticket.Id);
        await using var final = test.Data.Accounts.Database();
        Assert.NotNull((await final.Notifications.SingleAsync(x => x.Id == notification.Id)).DispatchedAt);
    }

    [PostgresFact]
    public async Task MarkReadDuringDispatchInvalidatesTheOldClaimAndQueuesANewSignal()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var recipient = await test.ClientAsync(owner);
        test.Connections.Add(new("read-race", Actor(recipient), DateTime.UtcNow.AddMinutes(5), () => { }));
        using var comment = await CommentAsync(admin, ticket.Id);
        var notification = Assert.Single((await recipient.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications"))!.Items);
        test.Sender.BeforeSend = async () =>
        {
            test.Sender.BeforeSend = null;
            using var response = await recipient.PutAsync($"/api/v1/notifications/{notification.Id}/read", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        };
        await test.DispatchAsync();
        await using (var database = test.Data.Accounts.Database())
        {
            var row = await database.Notifications.SingleAsync(x => x.Id == notification.Id);
            Assert.NotNull(row.ReadAt);
            Assert.Null(row.DispatchedAt);
        }
        await test.DispatchAsync();
        await using var final = test.Data.Accounts.Database();
        Assert.NotNull((await final.Notifications.SingleAsync(x => x.Id == notification.Id)).DispatchedAt);
    }

    [PostgresFact]
    public async Task UnknownLegacyAndWrongTicketRowsAreHiddenBeforeCountAndPaging()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        var other = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        await using var database = test.Data.Accounts.Database();
        var unknown = new TicketActivity { TicketId = ticket.Id, Action = "LegacyInternalSecret" };
        var known = new TicketActivity { TicketId = ticket.Id, Action = TicketActivityActions.PublicCommentAdded };
        database.TicketActivities.AddRange(unknown, known);
        await database.SaveChangesAsync();
        database.Notifications.AddRange(
            new Notification { UserId = test.Data.Accounts.AdminUser.Id, TicketId = ticket.Id, Title = "secret", Content = "secret" },
            new Notification { UserId = test.Data.Accounts.AdminUser.Id, TicketId = ticket.Id, TicketActivityId = unknown.Id, Title = "secret", Content = "secret" },
            new Notification { UserId = test.Data.Accounts.AdminUser.Id, TicketId = other.Id, TicketActivityId = known.Id, Title = "secret", Content = "secret" });
        await database.SaveChangesAsync();
        var page = (await admin.GetFromJsonAsync<PagedResult<NotificationDetails>>("/api/v1/notifications?pageSize=1"))!;
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [PostgresFact]
    public async Task FailureAfterNotificationSqlRollsBackCommentActivityAndNotifications()
    {
        var probe = new FailNotificationSave();
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, configure: services => services.Replace(
            ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(fixture.ConnectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
                .AddInterceptors(probe).Options))));
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var response = await CommentAsync(admin, ticket.Id);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(probe.Saved);
        Assert.DoesNotContain("notification-private-marker", await response.Content.ReadAsStringAsync());
        await using var database = test.Data.Accounts.Database();
        Assert.False(await database.TicketComments.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.False(await database.Notifications.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.Empty(test.Sender.Sent);
    }

    [PostgresFact]
    public async Task ManagedNotificationsBlockDestructiveMigrationRollback()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var response = await CommentAsync(admin, ticket.Id);
        await using var database = test.Data.Accounts.Database();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var rollback = new NpgsqlCommand(ModelTests.ReadSql("NotificationDelivery.Rollback.sql"), connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => rollback.ExecuteNonQueryAsync());
        await connection.CloseAsync();
        Assert.Equal("P0001", error.SqlState);
        Assert.Contains("20261028115316_NotificationDelivery", await database.Database.GetAppliedMigrationsAsync());
        Assert.True(await database.Notifications.AnyAsync(x => x.TicketId == ticket.Id));
    }

    [PostgresFact]
    public async Task AttachmentFinalizeCreatesNotificationsOnlyForCommittedMetadata()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var response = await AttachmentTestContext.SendFileAsync(admin, ticket.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var database = test.Data.Accounts.Database();
        var notification = await database.Notifications.SingleAsync(x => x.UserId == owner.Id && x.TicketId == ticket.Id);
        Assert.Equal(TicketActivityActions.PublicAttachmentAdded, (await database.TicketActivities.SingleAsync(x => x.Id == notification.TicketActivityId)).Action);
        Assert.Null(notification.DispatchedAt);
    }

    private sealed class FailNotificationSave : SaveChangesInterceptor
    {
        public bool Saved { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Notification>().Any(x => x.State == EntityState.Unchanged))
            { Saved = true; throw new InvalidOperationException("notification-private-marker"); }
            return ValueTask.FromResult(result);
        }
    }
}
