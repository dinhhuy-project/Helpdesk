using System.Diagnostics;
using System.Text.Json;
using be.Infrastructure.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class FrontendCommunityJourneyTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task PublicPortalPublishingFeedbackAndJavaScriptRealtimeWorkWithActualApi()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        await using (var database = test.Database())
        {
            var seed = new CommunityDemoInitializer(database, TimeProvider.System);
            Assert.True(await seed.SeedAsync());
            Assert.False(await seed.SeedAsync());
            Assert.Equal(8, await database.CommunityEntries.CountAsync(x => x.IsPublished));
        }
        using var backend = test.Factory.CreateClient(); backend.Timeout = Timeout.InfiniteTimeSpan;
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var bridge = builder.Build();
        bridge.Run(async context =>
        {
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding")) request.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
                if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) && !request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            using var response = await backend.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) context.Response.Headers[header.Key] = header.Value.ToArray();
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        await bridge.StartAsync();
        try
        {
            var root = FindRoot();
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "fe"), UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(Path.Combine(root, "fe", "scripts", "community-journey.mjs"));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start community JavaScript journey.");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { baseUrl = bridge.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(), admin = test.AdminCredentials }));
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180)); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            var diagnostic = await error;
            var location = System.Text.RegularExpressions.Regex.Match(diagnostic, @"community-(?:journey|browser)\.mjs:\d+:\d+").Value;
            Assert.True(process.ExitCode == 0, $"Community journey failed at {(location.Length > 0 ? location : "module/transport setup")}; credentials and response bodies omitted.");
            using var result = JsonDocument.Parse(await output);
            Assert.True(result.RootElement.GetProperty("publicVerified").GetBoolean());
            Assert.True(result.RootElement.GetProperty("realtimeVerified").GetBoolean());
            if (Environment.GetEnvironmentVariable("HELPDESK_RELEASE_ACCEPTANCE") == "true") Assert.True(result.RootElement.GetProperty("browserVerified").GetBoolean());
        }
        finally { await bridge.StopAsync(); }
    }
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "fe", "package.json"))) return dir.FullName;
        throw new InvalidOperationException("Helpdesk repository root not found.");
    }
}
