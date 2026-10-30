using System.Diagnostics;
using System.Text.Json;
using be.Domain.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class FrontendWorkflowJourneyTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task ActualFrontendClientsAssignProcessReopenConfirmAndCancelWithScopedLookups()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var allocator = await test.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketAssign);
        var leader = await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketAssign, PermissionCodes.TicketUpdateStatus);
        var agent = await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus, PermissionCodes.TicketCommentPublic);
        var requester = await test.CustomUserAsync(PermissionCodes.TicketReadOwn, PermissionCodes.TicketCreate, PermissionCodes.TicketCommentPublic,
            PermissionCodes.TicketConfirm, PermissionCodes.TicketReopen, PermissionCodes.TicketCancel);
        await test.MemberAsync(leader.Id, test.TeamA, leader: true);
        await test.MemberAsync(leader.Id, test.TeamB, leader: true);
        await test.MemberAsync(agent.Id, test.TeamA);
        await test.MemberAsync(agent.Id, test.TeamB);
        string agentName;
        await using (var database = test.Accounts.Database())
            agentName = await database.Users.Where(x => x.Id == agent.Id).Select(x => x.DisplayName).SingleAsync();
        using var backend = test.Accounts.Factory.CreateClient();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var bridge = builder.Build();
        bridge.Run(async context =>
        {
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
            start.ArgumentList.Add(Path.Combine(root, "fe", "scripts", "ticket-workflow-journey.mjs"));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new {
                baseUrl = bridge.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(),
                allocator = new { allocator.Email, allocator.Password }, leader = new { leader.Email, leader.Password },
                agent = new { agent.Email, agent.Password }, requester = new { requester.Email, requester.Password },
                agentId = agent.Id, agentName, teamA = test.TeamA, teamB = test.TeamB, categoryId = test.Category, prefix = test.Prefix
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180)); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            var diagnostic = await error;
            var location = System.Text.RegularExpressions.Regex.Match(diagnostic, @"ticket-workflow-(?:journey|browser)\.mjs:\d+:\d+").Value;
            var classification = System.Text.RegularExpressions.Regex.Match(diagnostic, @"status=(?:\d{3}|none), kind=(?:http|network|timeout|cancelled|unexpected|assertion/setup)").Value;
            Assert.True(process.ExitCode == 0, $"Frontend workflow runner failed at {(location.Length > 0 ? location : "module/transport setup")} ({classification}); details omitted.");
            using var result = JsonDocument.Parse(await output);
            Assert.True(result.RootElement.GetProperty("workflowCompleted").GetBoolean());
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
