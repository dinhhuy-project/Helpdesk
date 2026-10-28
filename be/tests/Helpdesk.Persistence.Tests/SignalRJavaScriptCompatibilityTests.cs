using System.Diagnostics;
using System.Text.Json;
using be.Domain.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public sealed class SignalRJavaScriptCompatibilityTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task JavaScript10ClientNegotiatesAuthenticatesRotatesAndChecksGroupsAgainstActualNet8Hub()
    {
        var root = FindRepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, "fe", "node_modules", "@microsoft", "signalr", "package.json")),
            "Run npm ci in fe/ before the JavaScript SignalR integration test.");
        Assert.Equal(8, typeof(Hub).Assembly.GetName().Version!.Major);
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, realSender: true);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        var outside = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        using var backend = test.Factory.CreateClient();
        backend.Timeout = Timeout.InfiniteTimeSpan;

        // TestServer has no TCP port. A loopback-only Kestrel bridge forwards HTTP/LongPolling
        // unchanged to the actual application, JWT handlers, database ACL and NotificationsHub.
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var bridge = builder.Build();
        bridge.Run(async context =>
        {
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
                context.Request.Path + context.Request.QueryString);
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
                request.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            using var response = await backend.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        await bridge.StartAsync();
        try
        {
            var url = bridge.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var start = new ProcessStartInfo("node")
            {
                WorkingDirectory = Path.Combine(root, "fe"), UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(Path.Combine(root, "fe", "scripts", "signalr-compatibility.mjs"));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new
            {
                baseUrl = url, email = owner.Email, password = owner.Password,
                ticketId = ticket.Id, outsideTicketId = outside.Id
            }));
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await error);
            using var result = JsonDocument.Parse(await output);
            Assert.StartsWith("10.", result.RootElement.GetProperty("clientVersion").GetString());
            Assert.True(result.RootElement.GetProperty("groupDenied").GetBoolean());
            Assert.True(result.RootElement.GetProperty("rotation").GetBoolean());
            Assert.True(result.RootElement.GetProperty("revoked").GetBoolean());
            Assert.True(result.RootElement.GetProperty("changedEvents").GetInt32() >= 2);
        }
        finally { await bridge.StopAsync(); }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "fe", "package.json"))) return directory.FullName;
        throw new InvalidOperationException("Run this test from the Helpdesk monorepo checkout.");
    }
}
