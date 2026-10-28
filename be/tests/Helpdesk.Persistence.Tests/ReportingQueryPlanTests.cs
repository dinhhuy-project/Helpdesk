using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Reporting;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class ReportingQueryPlanTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task ExplainActualAggregateQueriesOnRepresentativeDataBeforeAndAfterDateIndex()
    {
        await using var isolated = await PlanDatabase.CreateAsync(fixture.ConnectionString);
        var capture = new CaptureReports();
        await using var test = await NotificationTestContext.CreateAsync(isolated.ConnectionString, configure: services => services.Replace(
            ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(isolated.ConnectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
                .AddInterceptors(capture).Options))));
        var requester = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        await using var database = test.Data.Accounts.Database();
        var old = DateTime.UtcNow.AddYears(-2);
        var recent = DateTime.UtcNow.AddDays(-1);
        var adminId = test.Data.Accounts.AdminUser.Id;
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO helpdesk.tickets(id, requester_id, created_by_user_id, category_id,
                assigned_team_id, title, description, status, priority, created_at)
            SELECT gen_random_uuid(), CASE WHEN g % 4 = 0 THEN {requester.Id} ELSE {adminId} END, {adminId}, {test.Data.Category},
                CASE WHEN g % 2 = 0 THEN {test.Data.TeamA} ELSE {test.Data.TeamB} END,
                'Synthetic report Ticket', repeat('x', 400), 'New', 'Normal',
                CASE WHEN g <= 7900 THEN {old} ELSE {recent} END
            FROM generate_series(1,8000) g;
            """);
        var policy = new SlaPolicy { Name = "Plan policy", FirstResponseMinutes = 60, ResolutionMinutes = 240, IsActive = false };
        database.SlaPolicies.Add(policy); test.Data.OwnSlaPolicy(policy.Id);
        await database.SaveChangesAsync();
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO helpdesk.ticket_slas(ticket_id, sla_policy_id, applied_at, first_response_due_at, resolution_due_at,
                first_responded_at, policy_name, priority, first_response_minutes, resolution_minutes)
            SELECT id, {policy.Id}, created_at, created_at + interval '1 hour', created_at + interval '4 hours',
                CASE WHEN ticket_number % 2 = 0 THEN created_at + interval '30 minutes' ELSE NULL END,
                'Plan policy', 'Normal', 60, 240 FROM helpdesk.tickets WHERE category_id = {test.Data.Category};
            """);
        foreach (var id in await database.Tickets.Where(x => x.CategoryId == test.Data.Category).Select(x => x.Id).ToArrayAsync()) test.Data.OwnTicket(id);
        // This database belongs exclusively to this test. Never drop indexes in the application database.
        await database.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS helpdesk.ix_tickets_created_at");
        await database.Database.ExecuteSqlRawAsync("ANALYZE helpdesk.tickets");
        await database.Database.ExecuteSqlRawAsync("ANALYZE helpdesk.ticket_slas");
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var ownerClient = await test.ClientAsync(requester);
        using var agentClient = await test.ClientAsync(agent);
        var measurements = new List<PlanMeasurement>();
        foreach (var indexed in new[] { false, true })
        {
            if (indexed)
            {
                await database.Database.ExecuteSqlRawAsync("CREATE INDEX ix_tickets_created_at ON helpdesk.tickets(created_at)");
                await database.Database.ExecuteSqlRawAsync("ANALYZE helpdesk.tickets");
            }
            foreach (var (profile, client, expected) in new[] { ("all", admin, 100L), ("own", ownerClient, 25L), ("team", agentClient, 50L) })
            {
                capture.Commands.Clear();
                var summary = (await client.GetFromJsonAsync<DashboardReport>("/api/v1/dashboard"))!;
                Assert.Equal(expected, summary.Tickets.Total);
                await client.GetFromJsonAsync<GroupedTicketReport>("/api/v1/reports/tickets/teams?pageSize=1");
                await client.GetFromJsonAsync<DailyTicketReport>("/api/v1/reports/tickets/daily");
                var query = capture.Commands.First(x => x.Sql.Contains("Reports:dashboard", StringComparison.Ordinal));
                Assert.Contains("GROUP BY", query.Sql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("count(", query.Sql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("avg(", query.Sql, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ticket_comments", query.Sql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(capture.Commands, x => x.Sql.Contains("Reports:groups-page", StringComparison.Ordinal) && x.Sql.Contains("LIMIT", StringComparison.Ordinal));
                foreach (var command in capture.Commands.Where(x => !x.Sql.Contains("Reports:groups-count", StringComparison.Ordinal)))
                    measurements.Add(await ExplainAsync(isolated.ConnectionString, profile, indexed, command));
            }
        }
        Assert.Contains(measurements, x => x.Indexed && x.Profile == "all" && x.Indexes.Contains("ix_tickets_created_at"));
        var resultPath = Path.Combine(AppContext.BaseDirectory, "report-query-plan.json");
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { Tickets = 8000, RecentTickets = 100, Measurements = measurements }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<PlanMeasurement> ExplainAsync(string connectionString, string profile, bool indexed, CapturedSql sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql.Sql, connection);
        foreach (var parameter in sql.Parameters) command.Parameters.Add(parameter.Clone());
        using var json = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        var root = json.RootElement[0];
        var plan = root.GetProperty("Plan");
        var indexes = new HashSet<string>();
        var nodes = new HashSet<string>();
        Visit(plan);
        void Visit(JsonElement node)
        {
            nodes.Add(node.GetProperty("Node Type").GetString()!);
            if (node.TryGetProperty("Index Name", out var name)) indexes.Add(name.GetString()!);
            if (node.TryGetProperty("Plans", out var children)) foreach (var child in children.EnumerateArray()) Visit(child);
        }
        return new(profile, indexed, sql.Sql.Split('\n').First(x => x.Contains("Reports:", StringComparison.Ordinal)).Trim(),
            root.GetProperty("Execution Time").GetDouble(), plan.GetProperty("Actual Rows").GetInt64(),
            plan.GetProperty("Shared Hit Blocks").GetInt64(), indexes.Order().ToArray(), nodes.Order().ToArray());
    }
    private sealed record PlanMeasurement(string Profile, bool Indexed, string Query, double Milliseconds, long ResultRows, long SharedHitBlocks, string[] Indexes, string[] Nodes);
    private sealed record CapturedSql(string Sql, NpgsqlParameter[] Parameters);
    private sealed class CaptureReports : DbCommandInterceptor
    {
        public List<CapturedSql> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("-- Reports:", StringComparison.Ordinal))
                Commands.Add(new(command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(x => x.Clone()).ToArray()));
            return ValueTask.FromResult(result);
        }
    }
    private sealed class PlanDatabase(string administration, string name, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;
        public static async Task<PlanDatabase> CreateAsync(string administration)
        {
            if (new NpgsqlConnectionStringBuilder(administration).Database?.StartsWith("helpdesk_mapping_test_", StringComparison.Ordinal) != true) throw new InvalidOperationException("A disposable test database is required.");
            var name = "helpdesk_mapping_test_reports_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(administration);
            await connection.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection)) await command.ExecuteNonQueryAsync();
            var result = new PlanDatabase(administration, name, new NpgsqlConnectionStringBuilder(administration) { Database = name, Pooling = false }.ConnectionString);
            try
            {
                await using var database = ModelTests.CreateContext(result.ConnectionString);
                await database.Database.MigrateAsync();
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            if (!name.StartsWith("helpdesk_mapping_test_reports_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe report test cleanup.");
            await using var connection = new NpgsqlConnection(administration);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
