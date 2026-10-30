using System.Diagnostics;
using System.Text.Json;
using be.Domain.Constants;
using be.Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio.DataModel.Args;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class FrontendAttachmentJourneyTests(PostgresFixture fixture)
{
    [MinioFact]
    public async Task ActualFrontendUploadsAndDownloadsPrivateMinioFilesWithTicketCommentAndVisibilityScope()
    {
        await using var data = await TicketAccessTestContext.CreateAsync(fixture.ConnectionString);
        var requester = await data.CustomUserAsync(PermissionCodes.TicketReadOwn, PermissionCodes.TicketCreate,
            PermissionCodes.TicketCommentPublic, PermissionCodes.TicketCommentInternal);
        var agent = await data.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus,
            PermissionCodes.TicketCommentPublic, PermissionCodes.TicketCommentInternal);
        var outsider = await data.UserAsync(RoleNames.Requester);
        await data.MemberAsync(agent.Id, data.TeamA);
        var settings = MinioStorageTests.Settings();
        using var storageAdmin = MinioStorageTests.Client(settings);
        await using var factory = data.Accounts.Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Minio:Enabled"] = "true", ["Minio:CleanupEnabled"] = "false", ["Minio:Endpoint"] = settings.Endpoint,
                ["Minio:AccessKey"] = settings.AccessKey, ["Minio:SecretKey"] = settings.SecretKey, ["Minio:Bucket"] = settings.Bucket,
                ["Minio:HostOverride"] = "", ["Minio:UseSsl"] = "false", ["Minio:UploadsPerMinute"] = "1000" })));
        using var backend = factory.CreateClient();
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
        try
        {
            await factory.Services.GetRequiredService<MinioAttachmentStorage>().InitializeAsync(CancellationToken.None);
            await bridge.StartAsync();
            var root = FindRoot();
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "fe"), UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(Path.Combine(root, "fe", "scripts", "ticket-attachments-journey.mjs"));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new {
                baseUrl = bridge.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(),
                coordinator = new { data.Accounts.AdminUser.Email, data.Accounts.AdminUser.Password },
                requester = new { requester.Email, requester.Password }, agent = new { agent.Email, agent.Password },
                outsider = new { outsider.Email, outsider.Password }, agentId = agent.Id, requesterId = requester.Id,
                teamId = data.TeamA, categoryId = data.Category, prefix = data.Prefix
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180)); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            var diagnostic = await error;
            var location = System.Text.RegularExpressions.Regex.Match(diagnostic, @"ticket-attachments-(?:journey|browser)\.mjs:\d+:\d+").Value;
            var classification = System.Text.RegularExpressions.Regex.Match(diagnostic, @"status=(?:\d{3}|none), kind=(?:http|network|timeout|cancelled|unexpected|assertion/setup)").Value;
            Assert.True(process.ExitCode == 0, $"Frontend attachments runner failed at {(location.Length > 0 ? location : "module/transport setup")} ({classification}); details omitted.");
            using var result = JsonDocument.Parse(await output);
            Assert.True(result.RootElement.GetProperty("filesVerified").GetBoolean());
        }
        finally
        {
            await bridge.StopAsync();
            await using var database = data.Accounts.Database();
            foreach (var id in await database.Tickets.Where(x => x.Title.StartsWith(data.Prefix)).Select(x => x.Id).ToListAsync()) data.OwnTicket(id);
            // Only the GUID bucket created for this fixture is touched, on isolated loopback MinIO.
            if (await storageAdmin.BucketExistsAsync(new BucketExistsArgs().WithBucket(settings.Bucket)))
            {
                await foreach (var item in storageAdmin.ListObjectsEnumAsync(new ListObjectsArgs().WithBucket(settings.Bucket).WithRecursive(true)))
                    await storageAdmin.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(settings.Bucket).WithObject(item.Key));
                await storageAdmin.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(settings.Bucket));
            }
        }
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "fe", "package.json"))) return directory.FullName;
        throw new InvalidOperationException("Run from the Helpdesk monorepo.");
    }
}
