using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class SupportTeamMigrationTests(PostgresFixture fixture)
{
    [PostgresTheory]
    [InlineData("valid")]
    [InlineData("duplicate")]
    [InlineData("blank")]
    public async Task UpgradePreservesLegacyMembershipAndTicketAndRefusesInvalidNames(string scenario)
    {
        var databaseName = "helpdesk_mapping_test_teams_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = databaseName, Pooling = false };
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", administration))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using var context = ModelTests.CreateContext(settings.ConnectionString);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261026143539_DepartmentAdministration");
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            var department = Guid.NewGuid();
            var otherDepartment = Guid.NewGuid();
            var team = Guid.NewGuid();
            var user = Guid.NewGuid();
            var ticket = Guid.NewGuid();
            var category = Guid.NewGuid();
            var joined = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO organization.departments(id,name,is_active,created_at) VALUES(@department,'Legacy department',false,@created),(@other,'Other department',true,@created);
                INSERT INTO organization.support_teams(id,department_id,name,is_active,created_at)
                    VALUES(@team,@department,' Legacy Team ',false,@created),(@otherTeam,@other,'legacy team',true,@created);
                INSERT INTO identity.users(id,email,display_name,is_active,created_at) VALUES(@user,'legacy-team@example.test','Legacy member',true,@created);
                INSERT INTO organization.support_team_members(support_team_id,user_id,joined_at,is_active) VALUES(@team,@user,@joined,true);
                INSERT INTO helpdesk.ticket_categories(id,name,is_active,created_at) VALUES(@category,'Legacy category',true,@created);
                INSERT INTO helpdesk.tickets(id,title,description,requester_id,created_by_user_id,category_id,assigned_team_id,assigned_agent_id,status,priority,created_at)
                    VALUES(@ticket,'Legacy ticket','History',@user,@user,@category,@team,@user,'New','Normal',@created);
                """, connection))
            {
                insert.Parameters.AddWithValue("department", department);
                insert.Parameters.AddWithValue("other", otherDepartment);
                insert.Parameters.AddWithValue("otherTeam", Guid.NewGuid());
                insert.Parameters.AddWithValue("team", team);
                insert.Parameters.AddWithValue("user", user);
                insert.Parameters.AddWithValue("ticket", ticket);
                insert.Parameters.AddWithValue("category", category);
                insert.Parameters.AddWithValue("joined", joined);
                insert.Parameters.AddWithValue("created", DateTime.UtcNow);
                await insert.ExecuteNonQueryAsync();
            }
            if (scenario != "valid")
            {
                await using var invalid = new NpgsqlCommand("""
                    INSERT INTO organization.support_teams(id,department_id,name,is_active,created_at) VALUES(@id,@department,@name,true,@created)
                    """, connection);
                invalid.Parameters.AddWithValue("id", Guid.NewGuid());
                invalid.Parameters.AddWithValue("department", department);
                invalid.Parameters.AddWithValue("name", scenario == "blank" ? "   " : "legacy team");
                invalid.Parameters.AddWithValue("created", DateTime.UtcNow);
                await invalid.ExecuteNonQueryAsync();
                var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
                Assert.Equal("P0001", error.SqlState);
                Assert.DoesNotContain("Legacy Team", error.MessageText);
                Assert.Equal(3, (await context.Database.GetAppliedMigrationsAsync()).Count());
            }
            else
            {
                await using var upgrade = new NpgsqlCommand(ModelTests.ReadSql("SupportTeamAdministration.sql"), connection);
                await upgrade.ExecuteNonQueryAsync();
                await upgrade.ExecuteNonQueryAsync();
                var migrated = await context.SupportTeams.AsNoTracking().SingleAsync(x => x.Id == team);
                Assert.Equal(" Legacy Team ", migrated.Name);
                Assert.Equal("legacy team", migrated.NormalizedName);
                Assert.False(migrated.IsActive);
                Assert.True(migrated.Version > 0);
                var member = await context.SupportTeamMembers.AsNoTracking().SingleAsync(x => x.SupportTeamId == team && x.UserId == user);
                Assert.False(member.IsLeader); // Never infer authority from legacy roles/memberships.
                Assert.Null(member.UpdatedAt);
                Assert.True(member.Version > 0);
                await using var rollback = new NpgsqlCommand(ModelTests.ReadSql("SupportTeamAdministration.Rollback.sql"), connection);
                await rollback.ExecuteNonQueryAsync();
                Assert.Equal(3, (await context.Database.GetAppliedMigrationsAsync()).Count());
                await upgrade.ExecuteNonQueryAsync();
                Assert.Contains("20261026145925_SupportTeamAdministration", await context.Database.GetAppliedMigrationsAsync());
            }
            var legacyMember = await context.SupportTeamMembers.Where(x => x.SupportTeamId == team && x.UserId == user)
                .Select(x => new { x.IsActive, x.JoinedAt }).SingleAsync();
            Assert.True(legacyMember.IsActive);
            Assert.Equal(joined, legacyMember.JoinedAt);
            var historical = await context.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticket);
            Assert.Equal(team, historical.AssignedTeamId);
            Assert.Equal(user, historical.AssignedAgentId);
        }
        finally
        {
            if (!databaseName.StartsWith("helpdesk_mapping_test_teams_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the generated test database.");
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", administration);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
