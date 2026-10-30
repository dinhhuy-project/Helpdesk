using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Reporting;
using be.Contracts.Responses;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Domain.Rules;
using be.Infrastructure.Identity;
using be.Infrastructure.Persistence;
using be.Infrastructure.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class DemoWeekSeedTests(PostgresFixture fixture)
{
    private const string Password = "Only-used-in-isolated-tests-87!";
    private static readonly DateTime Anchor = new(2026, 10, 30, 11, 30, 0, DateTimeKind.Utc);

    [PostgresFact]
    public async Task WeekHasConsistentHistoriesSnapshotsAndAllDashboardDimensions()
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString);
        await using var database = test.Database();
        Assert.True(await Initializer(database).SeedAsync(Password));
        var tickets = await database.Tickets.ToListAsync();
        var slas = await database.TicketSlas.ToDictionaryAsync(x => x.TicketId);
        var activities = await database.TicketActivities.ToListAsync();
        var comments = await database.TicketComments.ToListAsync();
        Assert.Equal(144, tickets.Count);
        Assert.Equal(30, await database.Users.CountAsync());
        Assert.Equal(6, await database.Departments.CountAsync());
        Assert.Equal(3, await database.SupportTeams.CountAsync());
        Assert.Equal(9, await database.SupportTeamMembers.CountAsync());
        Assert.Equal(3, await database.SupportTeamMembers.CountAsync(x => x.IsLeader));
        Assert.Equal(15, await database.TicketCategories.CountAsync());
        Assert.Equal(7, tickets.Select(x => x.CreatedAt.AddHours(7).Date).Distinct().Count());
        Assert.Equal(7, tickets.Select(x => x.Status).Distinct().Count());
        Assert.Equal(4, tickets.Select(x => x.Priority).Distinct().Count());
        Assert.Contains(tickets, x => x.AssignedTeamId is null);
        Assert.Contains(tickets, x => x.AssignedTeamId != null && x.AssignedAgentId is null);
        Assert.Contains(tickets, x => !slas.ContainsKey(x.Id));
        Assert.Contains(slas.Values, x => x.FirstResponseBreachedAt != null);
        Assert.Contains(slas.Values, x => x.ResolutionBreachedAt != null);
        Assert.Contains(slas.Values, x => x.ResolvedAt != null && x.FirstResponseBreachedAt is null && x.ResolutionBreachedAt is null);
        Assert.Contains(slas.Values, x => x.FirstRespondedAt is null && x.ResolvedAt is null);
        Assert.False(await database.SlaPolicies.AnyAsync(x => x.IsActive));
        Assert.All(tickets, ticket =>
        {
            Assert.InRange(ticket.CreatedAt, Anchor.AddHours(7).Date.AddDays(-6).AddHours(-7), Anchor);
            Assert.True(ticket.TicketNumber > 0 && ticket.Version > 0);
            Assert.Equal(ticket.RequesterId, ticket.CreatedByUserId);
            var history = activities.Where(x => x.TicketId == ticket.Id).OrderBy(x => x.CreatedAt).ToList();
            Assert.Single(history, x => x.Action == TicketActivityActions.Created);
            var status = TicketStatus.New;
            foreach (var activity in history.Where(x => x.Action == TicketActivityActions.StatusChanged))
            {
                var old = Enum.Parse<TicketStatus>(JsonDocument.Parse(activity.OldValue!).RootElement.GetProperty("Status").GetString()!);
                var next = Enum.Parse<TicketStatus>(JsonDocument.Parse(activity.NewValue!).RootElement.GetProperty("Status").GetString()!);
                Assert.Equal(status, old);
                Assert.True(TicketWorkflow.CanTransition(old, next));
                status = next;
            }
            Assert.Equal(ticket.Status, status);
            if (slas.TryGetValue(ticket.Id, out var sla))
            {
                Assert.Equal(ticket.ResolvedAt, sla.ResolvedAt);
                Assert.Equal(ticket.CreatedAt.AddMinutes(sla.FirstResponseMinutes!.Value), sla.FirstResponseDueAt);
                Assert.Equal(ticket.CreatedAt.AddMinutes(sla.ResolutionMinutes!.Value), sla.ResolutionDueAt);
                var firstPublicResponse = comments.Where(x => x.TicketId == ticket.Id && !x.IsInternal && x.AuthorId == ticket.AssignedAgentId)
                    .OrderBy(x => x.CreatedAt).Select(x => (DateTime?)x.CreatedAt).FirstOrDefault();
                Assert.Equal(firstPublicResponse, sla.FirstRespondedAt);
            }
        });
        Assert.All(activities, activity => Assert.InRange(activity.CreatedAt, tickets.Single(x => x.Id == activity.TicketId).CreatedAt, Anchor));
        foreach (var comment in comments)
            Assert.Single(activities, x => x.TicketId == comment.TicketId &&
                x.Action == (comment.IsInternal ? TicketActivityActions.InternalNoteAdded : TicketActivityActions.PublicCommentAdded) &&
                JsonDocument.Parse(x.NewValue!).RootElement.GetProperty("CommentId").GetGuid() == comment.Id);
        var notifications = await database.Notifications.ToListAsync();
        Assert.NotEmpty(notifications);
        Assert.Contains(notifications, x => x.ReadAt is null);
        Assert.Contains(notifications, x => x.ReadAt != null);
        Assert.All(notifications, x => Assert.NotNull(x.DispatchedAt));
        Assert.All(notifications.Where(n => activities.Any(a => a.Id == n.TicketActivityId && a.Action == TicketActivityActions.InternalNoteAdded)),
            n => Assert.NotEqual(tickets.Single(t => t.Id == n.TicketId).RequesterId, n.UserId));
    }

    [PostgresFact]
    public async Task RealLoginsEnforceOwnTeamAllScopesAndSeparateAccountManagementFromGrants()
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString);
        await using (var seed = test.Database()) await Initializer(seed).SeedAsync(Password);
        await using var factory = new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Helpdesk"] = test.ConnectionString,
                ["Database:HostOverride"] = "", ["AuthRateLimit:PermitLimit"] = "100", ["Identity:SeedOnStartup"] = "false" })));
        await using var database = test.Database();
        foreach (var login in new[] { "requester.01", "agent.it.1", "lead.hr", "admin", "reports", "accounts" })
        {
            using var client = factory.CreateClient();
            using var auth = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = $"{login}@{DemoWeekCatalog.Domain}", password = Password });
            Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
            var session = (await auth.Content.ReadFromJsonAsync<LoginResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            using var response = await client.GetAsync("/api/v1/dashboard?createdFrom=2026-10-23T00%3A00%3A00Z&createdTo=2026-11-01T00%3A00%3A00Z");
            if (login == "accounts")
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                using var accounts = await client.GetAsync("/api/v1/users");
                Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);
                using var grants = await client.GetAsync("/api/v1/identity/roles");
                Assert.Equal(HttpStatusCode.Forbidden, grants.StatusCode);
                continue;
            }
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var user = await database.Users.SingleAsync(x => x.Email == $"{login}@{DemoWeekCatalog.Domain}");
            var expected = database.Tickets.AsQueryable();
            if (login == "requester.01") expected = expected.Where(x => x.RequesterId == user.Id);
            if (login is "agent.it.1" or "lead.hr")
            {
                var teamId = await database.SupportTeamMembers.Where(x => x.UserId == user.Id).Select(x => x.SupportTeamId).SingleAsync();
                expected = expected.Where(x => x.AssignedTeamId == teamId || x.RequesterId == user.Id);
            }
            Assert.Equal(await expected.CountAsync(), (await response.Content.ReadFromJsonAsync<DashboardReport>())!.Tickets.Total);
            if (login == "agent.it.1") Assert.Equal(2, session.User.Roles.Count);
            if (login == "reports")
            {
                Assert.Equal(new[] { PermissionCodes.TicketReadAll }, session.User.Permissions);
                var internalTicket = await database.TicketComments.Where(x => x.IsInternal).Select(x => x.TicketId).FirstAsync();
                using var discussion = await client.GetAsync($"/api/v1/tickets/{internalTicket}/comments?pageSize=100");
                Assert.Equal(HttpStatusCode.OK, discussion.StatusCode);
                var page = await discussion.Content.ReadFromJsonAsync<JsonElement>();
                Assert.All(page.GetProperty("items").EnumerateArray(), x => Assert.False(x.GetProperty("isInternal").GetBoolean()));
            }
            if (login == "requester.01")
            {
                var own = await expected.FirstAsync(x => x.Status != TicketStatus.New);
                using var discussion = await client.GetAsync($"/api/v1/tickets/{own.Id}/comments?pageSize=100");
                Assert.Equal(HttpStatusCode.OK, discussion.StatusCode);
                var body = await discussion.Content.ReadFromJsonAsync<JsonElement>();
                Assert.All(body.GetProperty("items").EnumerateArray(), x => Assert.False(x.GetProperty("isInternal").GetBoolean()));
                var hidden = await database.Tickets.FirstAsync(x => x.RequesterId != user.Id);
                using var forbidden = await client.GetAsync($"/api/v1/tickets/{hidden.Id}");
                Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
            }
        }
    }

    [PostgresFact]
    public async Task RerunPreservesExistingUsersGrantsPoliciesAndEditedDemoData()
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString);
        await using var database = test.Database();
        var existing = new User { Email = "existing@example.test", DisplayName = "Existing employee", PasswordHash = "unchanged" };
        var live = new SlaPolicy { Name = "Live policy", Priority = TicketPriority.Urgent, FirstResponseMinutes = 20, ResolutionMinutes = 100 };
        database.Users.Add(existing);
        database.SlaPolicies.Add(live);
        await database.SaveChangesAsync();
        var version = existing.Version;
        Assert.True(await Initializer(database).SeedAsync(Password));
        var ticket = await database.Tickets.FirstAsync();
        ticket.Title = "Edited after demo seed";
        var grant = await database.RolePermissions.FirstAsync();
        database.RolePermissions.Remove(grant);
        await database.SaveChangesAsync();
        await using var rerun = test.Database();
        Assert.False(await Initializer(rerun).SeedAsync(""));
        Assert.Equal(31, await rerun.Users.CountAsync());
        Assert.Equal("unchanged", (await rerun.Users.SingleAsync(x => x.Id == existing.Id)).PasswordHash);
        Assert.Equal(version, (await rerun.Users.SingleAsync(x => x.Id == existing.Id)).Version);
        Assert.Equal("Edited after demo seed", (await rerun.Tickets.SingleAsync(x => x.Id == ticket.Id)).Title);
        Assert.False(await rerun.RolePermissions.AnyAsync(x => x.RoleId == grant.RoleId && x.PermissionId == grant.PermissionId));
        Assert.Equal(live.Id, (await rerun.SlaPolicies.SingleAsync(x => x.IsActive)).Id);
        Assert.False(await rerun.Notifications.AnyAsync(x => x.UserId == existing.Id));
    }

    [PostgresTheory]
    [InlineData("collision")]
    [InlineData("grants")]
    [InlineData("password")]
    public async Task InvalidPreconditionsDoNotPartiallySeed(string reason)
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString);
        await using var database = test.Database();
        if (reason == "collision") database.Users.Add(new User { Email = DemoWeekCatalog.Accounts[0].Email, DisplayName = "Existing account" });
        if (reason == "grants") database.RolePermissions.Remove(await database.RolePermissions.FirstAsync());
        await database.SaveChangesAsync();
        await Assert.ThrowsAsync<IdentityInitializationException>(() => Initializer(database).SeedAsync(reason == "password" ? "short" : Password));
        await using var verify = test.Database();
        Assert.Empty(await verify.Departments.ToListAsync());
        Assert.Empty(await verify.Tickets.ToListAsync());
        Assert.Equal(4, await verify.Roles.CountAsync());
        Assert.False(await verify.IdentityAuditLogs.AnyAsync(x => x.Id == DemoWeekInitializer.MarkerId));
    }

    [PostgresFact]
    public async Task FailureAfterHistorySqlRollsBackAndConcurrentRetryCreatesExactlyOneWeek()
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString);
        await using (var failed = test.Database(new FailAfterHistorySave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Initializer(failed).SeedAsync(Password));
        await using var first = test.Database();
        Assert.Empty(await first.Users.ToListAsync());
        Assert.Empty(await first.Tickets.ToListAsync());
        Assert.Equal(4, await first.Roles.CountAsync());
        await using var second = test.Database();
        var results = await Task.WhenAll(Initializer(first).SeedAsync(Password), Initializer(second).SeedAsync(Password));
        Assert.Single(results, x => x);
        Assert.Equal(144, await first.Tickets.CountAsync());
        Assert.Single(await first.IdentityAuditLogs.Where(x => x.Id == DemoWeekInitializer.MarkerId).ToListAsync());
    }

    private static DemoWeekInitializer Initializer(HelpdeskDbContext database) => new(database, new PasswordHasher<User>(), new SeedClock());
    private sealed class SeedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Anchor); }
    private sealed class FailAfterHistorySave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<TicketActivity>().Any()) throw new InvalidOperationException("Injected failure after history SQL.");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class SeedDatabase(string administration, string name, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString => connectionString;
        public HelpdeskDbContext Database(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk")).AddInterceptors(interceptors).Options);
        public static async Task<SeedDatabase> CreateAsync(string administration)
        {
            var name = "helpdesk_demo_test_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(administration);
            await connection.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection)) await command.ExecuteNonQueryAsync();
            var settings = new NpgsqlConnectionStringBuilder(administration) { Database = name, Pooling = false };
            var result = new SeedDatabase(administration, name, settings.ConnectionString);
            try
            {
                await using var database = result.Database();
                await database.Database.MigrateAsync();
                await new IdentityInitializer(database, new PasswordHasher<User>()).SeedAsync();
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            if (!name.StartsWith("helpdesk_demo_test_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test database cleanup.");
            await using var connection = new NpgsqlConnection(administration);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
