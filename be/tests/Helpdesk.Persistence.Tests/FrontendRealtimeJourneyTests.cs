using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using be.Domain.Constants;
using be.Infrastructure.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class FrontendRealtimeJourneyTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task ActualJavaScriptLifecycleReconnectsRotatesAndRechecksGroupsWithNet8()
    {
        await using var testContext = await NotificationTestContext.CreateAsync(fixture.ConnectionString, realSender: true);
        var test = testContext.Data;
        var agent = await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus, PermissionCodes.TicketCommentPublic, PermissionCodes.TicketCommentInternal);
        var requester = await test.CustomUserAsync(PermissionCodes.TicketReadOwn, PermissionCodes.TicketCreate, PermissionCodes.TicketCommentPublic,
            PermissionCodes.TicketCommentInternal);
        await test.MemberAsync(agent.Id, test.TeamA);
        var outsider = await test.CustomUserAsync(PermissionCodes.TicketReadOwn);
        using var backend = testContext.Factory.CreateClient();
        backend.Timeout = Timeout.InfiniteTimeSpan;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var bridge = builder.Build();
        bridge.Run(async context =>
        {
            // Loopback-only controls for this disposable fixture; these are not production endpoints.
            if (context.Request.Path.StartsWithSegments("/__tests/realtime"))
            {
                using var verification = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
                verification.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers.Authorization.ToArray());
                using var verified = await backend.SendAsync(verification, context.RequestAborted);
                if (!verified.IsSuccessStatusCode) { context.Response.StatusCode = 401; return; }
                var original = new JwtSecurityTokenHandler().ReadJwtToken(context.Request.Headers.Authorization.ToString()[7..]);
                var userId = Guid.Parse(original.Subject);
                var action = context.Request.Path.Value!.Split('/').Last();
                if (action is "short-token" or "expired-token")
                {
                    var now = DateTime.UtcNow;
                    var expiry = action == "short-token" ? now.AddSeconds(4) : now.AddMinutes(-2);
                    var token = new JwtSecurityToken(AuthTestSettings.Issuer, AuthTestSettings.Audience,
                        original.Claims.Where(x => x.Type is not "exp" and not "nbf"), now.AddMinutes(-5), expiry,
                        new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTestSettings.SigningKey)), SecurityAlgorithms.HmacSha256));
                    token.Header["typ"] = "at+jwt";
                    await context.Response.WriteAsJsonAsync(new { token = new JwtSecurityTokenHandler().WriteToken(token), expiresAt = expiry });
                }
                else if (action == "dispatch") { await testContext.DispatchAsync(); context.Response.StatusCode = 204; }
                else if (action == "disconnect")
                {
                    foreach (var connection in testContext.Connections.Snapshot().Where(x => x.Actor.UserId == userId)) connection.Abort();
                    context.Response.StatusCode = 204;
                }
                else if (action == "connections")
                {
                    await using var scope = testContext.Factory.Services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<RealtimeConnectionValidator>().RunOnceAsync(CancellationToken.None);
                    await context.Response.WriteAsJsonAsync(testContext.Connections.Snapshot().Select(x => new { x.Id, userId = x.Actor.UserId, tickets = x.Tickets }));
                }
                else context.Response.StatusCode = 404;
                return;
            }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding")) request.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
                if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) && !request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            using var response = await backend.SendAsync(request, context.RequestAborted);
            var body = await response.Content.ReadAsByteArrayAsync(context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            if (response.StatusCode is not System.Net.HttpStatusCode.NoContent and not System.Net.HttpStatusCode.NotModified)
            {
                context.Response.ContentLength = body.Length;
                await context.Response.Body.WriteAsync(body, context.RequestAborted);
            }
        });
        await bridge.StartAsync();
        try
        {
            var root = FindRoot();
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "fe"), UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(Path.Combine(root, "fe", "scripts", "notification-realtime-journey.mjs"));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new {
                baseUrl = bridge.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(),
                coordinator = new { test.Accounts.AdminUser.Email, test.Accounts.AdminUser.Password },
                agent = new { agent.Email, agent.Password }, requester = new { requester.Email, requester.Password },
                outsider = new { outsider.Email, outsider.Password }, agentId = agent.Id,
                teamId = test.TeamA, categoryId = test.Category, prefix = test.Prefix
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180)); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            var diagnostic = await error;
            var location = System.Text.RegularExpressions.Regex.Match(diagnostic, @"notification-realtime-(?:journey|browser)\.mjs:\d+:\d+").Value;
            var classification = System.Text.RegularExpressions.Regex.Match(diagnostic, @"status=(?:\d{3}|none), kind=(?:http|network|timeout|cancelled|unexpected|assertion/setup)").Value;
            Assert.True(process.ExitCode == 0, $"Frontend realtime runner failed at {(location.Length > 0 ? location : "module/transport setup")} ({classification}); details omitted.");
            using var result = JsonDocument.Parse(await output);
            Assert.True(result.RootElement.GetProperty("realtimeVerified").GetBoolean());
        }
        finally
        {
            await bridge.StopAsync();
            await using var database = test.Accounts.Database();
            // Register even a partially completed browser/client journey for the fixture's ordered cleanup.
            foreach (var id in await database.Tickets.Where(x => x.Title.StartsWith(test.Prefix)).Select(x => x.Id).ToListAsync()) test.OwnTicket(id);
        }
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "fe", "package.json"))) return directory.FullName;
        throw new InvalidOperationException("Run from the Helpdesk monorepo.");
    }
}
