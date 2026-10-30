using System.Diagnostics;
using System.Text.Json;
using be.Domain.Constants;
using be.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Helpdesk.Persistence.Tests.AccountAdministrationTests;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class FrontendAccountJourneyTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task ActualFrontendClientsManageUsersRolesDepartmentsAndRevokedSessions()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var manager = await test.NewUserAsync();
        await test.AssignAsync(manager.Id, await test.CustomRoleAsync(PermissionCodes.UserManage));
        var roleManager = await test.NewUserAsync();
        var requester = (await test.RolesAsync()).Single(x => x.Name == RoleNames.Requester);
        await test.AssignAsync(roleManager.Id, await test.CustomRoleAsync([PermissionCodes.RoleManage, .. requester.Permissions]));
        var ordinaryRoleId = await test.CustomRoleAsync(PermissionCodes.TicketReadOwn);
        var prefix = $"fe-account-{Guid.NewGuid():N}";
        var department = new Department { Name = prefix, IsActive = true };
        await using (var database = test.Database()) { database.Departments.Add(department); await database.SaveChangesAsync(); }
        using var backend = test.Factory.CreateClient();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var bridge = builder.Build();
        var bridgeErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        bridge.Run(async context =>
        {
            try
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
                // Kestrel rejects even an empty WriteAsync for 204. A TestServer bridge must
                // finish these responses without writing, otherwise its pooled socket resets.
                if (response.StatusCode is not System.Net.HttpStatusCode.NoContent and not System.Net.HttpStatusCode.NotModified)
                {
                    context.Response.ContentLength = body.Length;
                    await context.Response.Body.WriteAsync(body, context.RequestAborted);
                }
            }
            catch (Exception exception)
            {
                var category = exception.Message.Contains("Content-Length", StringComparison.OrdinalIgnoreCase) ? "content length" :
                    exception.Message.Contains("non-ASCII", StringComparison.OrdinalIgnoreCase) ? "non-ASCII header" :
                    exception.Message.Contains("status code", StringComparison.OrdinalIgnoreCase) ? "status code" : "other";
                bridgeErrors.Enqueue(exception.GetType().Name + ": " + category);
                throw;
            }
        });
        await bridge.StartAsync();
        try
        {
            var root = FindRoot();
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "fe"), UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(Path.Combine(root, "fe", "scripts", "account-administration-journey.mjs"));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new {
                baseUrl = bridge.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(),
                admin = new { test.AdminUser.Email, test.AdminUser.Password }, manager = new { manager.Email, manager.Password },
                roleManager = new { roleManager.Email, roleManager.Password }, departmentId = department.Id,
                departmentName = department.Name, ordinaryRoleId, prefix
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(150)); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            var diagnostic = await error;
            var location = System.Text.RegularExpressions.Regex.Match(diagnostic, @"account-administration-(?:journey|browser)\.mjs:\d+:\d+").Value;
            var classification = System.Text.RegularExpressions.Regex.Match(diagnostic, @"status=(?:\d{3}|none), kind=(?:http|network|timeout|cancelled|unexpected|assertion/setup)").Value;
            Assert.True(process.ExitCode == 0, $"Frontend account runner failed at {(location.Length > 0 ? location : "module/transport setup")} ({classification}); bridge: {string.Join("; ", bridgeErrors)}; details omitted.");
            using var result = JsonDocument.Parse(await output);
            Assert.True(result.RootElement.GetProperty("revokedSessionsHandled").GetBoolean());
        }
        finally
        {
            await bridge.StopAsync();
            await using var database = test.Database();
            var created = await database.Users.Where(x => x.NormalizedEmail.StartsWith(prefix)).Select(x => x.Id).ToArrayAsync();
            foreach (var id in created) test.Own(id);
            await database.Users.Where(x => x.DepartmentId == department.Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DepartmentId, (Guid?)null));
            await database.Departments.Where(x => x.Id == department.Id).ExecuteDeleteAsync();
        }
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "fe", "package.json"))) return directory.FullName;
        throw new InvalidOperationException("Run from the Helpdesk monorepo.");
    }
}
