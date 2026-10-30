using System.Diagnostics;
using System.Text.Json;
using be.Domain.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class FrontendDiscussionJourneyTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task ActualFrontendClientsCreateExchangeAndKeepRequesterCachePublic()
    {
        await using var test = await TicketAccessTestContext.CreateAsync(fixture.ConnectionString);
        // A requester with admin grants must still never receive internal notes on their own Ticket.
        var owner = await test.UserAsync(RoleNames.Requester);
        await test.Accounts.AssignAsync(owner.Id, await test.Accounts.RoleIdAsync(RoleNames.SystemAdmin));
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        using var backend = test.Accounts.Factory.CreateClient();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var bridge = builder.Build();
        bridge.Run(async context =>
        {
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding")) request.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
                if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) && !request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            using var response = await backend.SendAsync(request, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) context.Response.Headers[header.Key] = header.Value.ToArray();
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        await bridge.StartAsync();
        try
        {
            var root = FindRoot();
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "fe"), UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(Path.Combine(root, "fe", "scripts", "ticket-discussion-journey.mjs"));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new {
                baseUrl = bridge.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(),
                requester = new { owner.Email, owner.Password }, agent = new { agent.Email, agent.Password },
                admin = new { test.Accounts.AdminUser.Email, test.Accounts.AdminUser.Password }, categoryId = test.Category, teamId = test.TeamA, prefix = test.Prefix
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120)); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            // Browser transport diagnostics may contain Bearer headers; report only a script location.
            var diagnostic = await error;
            var location = System.Text.RegularExpressions.Regex.Match(diagnostic, @"ticket-discussion-(?:journey|browser)\.mjs:\d+:\d+").Value;
            Assert.True(process.ExitCode == 0, $"Frontend discussion runner failed at {(location.Length > 0 ? location : "module/transport setup")}; details omitted.");
            using var result = JsonDocument.Parse(await output);
            test.OwnTicket(result.RootElement.GetProperty("ticketId").GetGuid());
            if (result.RootElement.TryGetProperty("browserTicketId", out var browserTicket)) test.OwnTicket(browserTicket.GetGuid());
            Assert.True(result.RootElement.GetProperty("requesterCachePublic").GetBoolean());
        }
        finally
        {
            await bridge.StopAsync();
            // Also own created tickets if a failed Node/browser assertion prevented its final receipt.
            await using var database = test.Accounts.Database();
            var ids = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToArrayAsync(
                database.Tickets.Where(x => x.RequesterId == owner.Id).Select(x => x.Id));
            foreach (var id in ids) test.OwnTicket(id);
        }
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "fe", "package.json"))) return directory.FullName;
        throw new InvalidOperationException("Run from the Helpdesk monorepo.");
    }
}
