using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using be.Contracts.Responses;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Identity;
using be.Infrastructure.Persistence;
using be.Infrastructure.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class DevelopmentDataSeedTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task StartupBootstrapsIdentityThenSamplesAndLoginCanReadTickets()
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString, initializeIdentity: false);
        for (var startup = 0; startup < 2; startup++)
        {
            await using var factory = new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Helpdesk"] = test.ConnectionString,
                    [IdentityStartupSeeder.ConfigurationKey] = "true", [IdentityStartupSeeder.BootstrapConfigurationKey] = "true",
                    [DevelopmentDataInitializer.ConfigurationKey] = "true",
                    ["BootstrapAdmin:Email"] = test.Admin.Email, ["BootstrapAdmin:DisplayName"] = test.Admin.DisplayName,
                    ["BootstrapAdmin:Password"] = test.Admin.Password
                })));
            using var client = factory.CreateClient();
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = test.Admin.Email, password = test.Admin.Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            using var response = await client.GetAsync("/api/v1/tickets?pageSize=100");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
        }
        await using var database = test.Database();
        Assert.Equal(3, await database.Departments.CountAsync());
        Assert.Equal(3, await database.SupportTeams.CountAsync());
        Assert.Equal(3, await database.SupportTeamMembers.CountAsync(x => x.IsActive && x.IsLeader));
        Assert.Equal(7, await database.TicketCategories.CountAsync());
        Assert.Equal(4, await database.TicketCategories.CountAsync(x => x.ParentId != null));
        Assert.Equal(1, await database.Users.CountAsync());
        Assert.Null((await database.Users.SingleAsync()).DepartmentId);
        var tickets = await database.Tickets.OrderBy(x => x.TicketNumber).ToListAsync();
        Assert.Equal(5, tickets.Select(x => x.TicketNumber).Distinct().Count());
        Assert.All(tickets, ticket => { Assert.True(ticket.TicketNumber > 0); Assert.True(ticket.Version > 0); Assert.Equal(ticket.RequesterId, ticket.CreatedByUserId); });
        Assert.Equal(new[] { TicketStatus.New, TicketStatus.InProgress, TicketStatus.WaitingForRequester, TicketStatus.Resolved, TicketStatus.Closed }, tickets.OrderBy(x => x.Status).Select(x => x.Status));
        Assert.All(tickets.Where(x => x.Status is TicketStatus.Resolved or TicketStatus.Closed), ticket =>
        { Assert.False(string.IsNullOrWhiteSpace(ticket.ResolutionSummary)); Assert.NotNull(ticket.ResolvedAt); });
        Assert.NotNull(tickets.Single(x => x.Status == TicketStatus.Closed).ClosedAt);
        var activities = await database.TicketActivities.ToListAsync();
        Assert.Equal(17, activities.Count);
        Assert.Equal(5, activities.Count(x => x.Action == TicketActivityActions.Created));
        Assert.All(activities, activity => Assert.Null(activity.ActorId));
        foreach (var ticket in tickets)
        {
            var created = activities.Single(x => x.TicketId == ticket.Id && x.Action == TicketActivityActions.Created);
            Assert.Equal(ticket.TicketNumber, JsonDocument.Parse(created.NewValue!).RootElement.GetProperty("TicketNumber").GetInt64());
        }
        Assert.Empty(await database.TicketSlas.ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentSeedsCreateOneBundleAndRerunsPreserveEditsAndGrants()
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString);
        await using var first = test.Database();
        await using var second = test.Database();
        var results = await Task.WhenAll(Initializer(first).SeedAsync(), Initializer(second).SeedAsync());
        Assert.Single(results, created => created);
        await using var edited = test.Database();
        var department = await edited.Departments.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstAsync();
        department.Name = "Tên do người dùng sửa";
        department.IsActive = false;
        var ticket = await edited.Tickets.OrderBy(x => x.TicketNumber).FirstAsync();
        ticket.Title = "Ticket đã sửa";
        var grant = await edited.RolePermissions.FirstAsync();
        edited.RolePermissions.Remove(grant);
        await edited.SaveChangesAsync();
        var version = ticket.Version;
        var passwordHash = (await edited.Users.SingleAsync()).PasswordHash;
        await using var rerun = test.Database();
        Assert.False(await Initializer(rerun).SeedAsync());
        Assert.Equal("Tên do người dùng sửa", (await rerun.Departments.SingleAsync(x => x.Id == department.Id)).Name);
        Assert.False((await rerun.Departments.SingleAsync(x => x.Id == department.Id)).IsActive);
        Assert.Equal("Ticket đã sửa", (await rerun.Tickets.SingleAsync(x => x.Id == ticket.Id)).Title);
        Assert.Equal(version, (await rerun.Tickets.SingleAsync(x => x.Id == ticket.Id)).Version);
        Assert.Equal(passwordHash, (await rerun.Users.SingleAsync()).PasswordHash);
        Assert.False(await rerun.RolePermissions.AnyAsync(x => x.RoleId == grant.RoleId && x.PermissionId == grant.PermissionId));
        Assert.Equal(17, await rerun.TicketActivities.CountAsync());
    }

    [PostgresTheory]
    [InlineData("department")]
    [InlineData("category")]
    public async Task ExistingBusinessDataSkipsWholeBundle(string existing)
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString, initializeIdentity: false);
        await using var database = test.Database();
        if (existing == "department") database.Departments.Add(new Department { Name = "Existing department" });
        else database.TicketCategories.Add(new TicketCategory { Name = "Existing category" });
        await database.SaveChangesAsync();
        Assert.False(await Initializer(database).SeedAsync());
        Assert.False(await database.Tickets.AnyAsync());
        Assert.False(await database.SupportTeams.AnyAsync());
        Assert.False(await database.Users.AnyAsync());
        Assert.Equal(1, await database.Departments.CountAsync() + await database.TicketCategories.CountAsync());
    }

    [PostgresTheory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("grant")]
    public async Task MissingOrIneligibleAdminCannotCreatePartialData(string scenario)
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString, initializeIdentity: scenario != "missing");
        await using var database = test.Database();
        if (scenario == "inactive") (await database.Users.SingleAsync()).IsActive = false;
        if (scenario == "grant")
        {
            var permissionId = await database.Permissions.Where(x => x.Code == PermissionCodes.TicketUpdateStatus).Select(x => x.Id).SingleAsync();
            database.RolePermissions.RemoveRange(await database.RolePermissions.Where(x => x.PermissionId == permissionId).ToListAsync());
        }
        await database.SaveChangesAsync();
        await Assert.ThrowsAsync<IdentityInitializationException>(() => Initializer(database).SeedAsync());
        Assert.False(await database.Departments.AnyAsync());
        Assert.False(await database.Tickets.AnyAsync());
    }

    [PostgresFact]
    public async Task FailureAfterActivitySqlRollsBackWholeBundleAndRetrySucceeds()
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString);
        var probe = new FailAfterActivitySave();
        await using (var database = test.Database(probe))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Initializer(database).SeedAsync());
        Assert.True(probe.Saved);
        await using var verify = test.Database();
        Assert.False(await verify.Departments.AnyAsync());
        Assert.False(await verify.SupportTeams.AnyAsync());
        Assert.False(await verify.SupportTeamMembers.AnyAsync());
        Assert.False(await verify.TicketCategories.AnyAsync());
        Assert.False(await verify.Tickets.AnyAsync());
        Assert.False(await verify.TicketActivities.AnyAsync());
        Assert.True(await Initializer(verify).SeedAsync());
    }

    [PostgresFact]
    public async Task PendingMigrationIsRejectedWithoutApplyingIt()
    {
        await using var test = await SeedDatabase.CreateAsync(fixture.ConnectionString, initializeIdentity: false);
        await using var database = test.Database();
        await database.GetService<IMigrator>().MigrateAsync("20261026151829_TicketCategoryAdministration");
        await Assert.ThrowsAsync<IdentityInitializationException>(() => Initializer(database).SeedAsync());
        Assert.Single(await database.Database.GetPendingMigrationsAsync());
        Assert.False(await database.Departments.AnyAsync());
    }

    [Fact]
    public async Task DirectInitializerRejectsProductionBeforeConnectingToDatabase()
    {
        await using var database = ModelTests.CreateContext();
        var initializer = new DevelopmentDataInitializer(database, new SeedEnvironment { EnvironmentName = "Production" }, TimeProvider.System);
        await Assert.ThrowsAsync<IdentityInitializationException>(() => initializer.SeedAsync());
    }

    private static DevelopmentDataInitializer Initializer(HelpdeskDbContext database) => new(database, new SeedEnvironment(), TimeProvider.System);
    private sealed class SeedEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class FailAfterActivitySave : SaveChangesInterceptor
    {
        internal bool Saved { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TicketActivity>().Any())
            { Saved = true; throw new InvalidOperationException("Injected failure after SQL."); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SeedDatabase(string administrationConnection, string databaseName, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString => connectionString;
        public BootstrapAdminOptions Admin { get; } = new()
        {
            Email = "sample-admin@example.test", DisplayName = "Test administrator", Password = Guid.NewGuid().ToString("N")
        };
        public HelpdeskDbContext Database(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
            .AddInterceptors(interceptors).Options);
        public static async Task<SeedDatabase> CreateAsync(string administrationConnection, bool initializeIdentity = true)
        {
            var name = "helpdesk_mapping_test_samples_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(administrationConnection);
            await connection.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection)) await command.ExecuteNonQueryAsync();
            var settings = new NpgsqlConnectionStringBuilder(administrationConnection) { Database = name, Pooling = false };
            var result = new SeedDatabase(administrationConnection, name, settings.ConnectionString);
            try
            {
                await using var database = result.Database();
                await database.Database.MigrateAsync();
                if (initializeIdentity)
                {
                    var identity = new IdentityInitializer(database, new PasswordHasher<User>());
                    await identity.SeedAsync();
                    await identity.BootstrapAdminAsync(result.Admin);
                }
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            if (!databaseName.StartsWith("helpdesk_mapping_test_samples_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test database cleanup.");
            await using var connection = new NpgsqlConnection(administrationConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
