using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class DepartmentMigrationTests(PostgresFixture fixture)
{
    [PostgresTheory]
    [InlineData("valid")]
    [InlineData("duplicate")]
    [InlineData("blank")]
    public async Task UpgradePreservesLegacyReferencesAndRefusesInvalidNames(string scenario)
    {
        var databaseName = "helpdesk_mapping_test_departments_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = databaseName, Pooling = false };
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", administration))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using var context = ModelTests.CreateContext(settings.ConnectionString);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261026134032_AccountAdministration");
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            var department = Guid.NewGuid();
            var user = Guid.NewGuid();
            var team = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO organization.departments(id,name,description,is_active,created_at)
                    VALUES(@department,' Legacy Finance ','Historical description',false,@created);
                INSERT INTO identity.users(id,email,display_name,department_id,is_active,created_at)
                    VALUES(@user,'legacy-department@example.test','Legacy user',@department,true,@created);
                INSERT INTO organization.support_teams(id,department_id,name,is_active,created_at)
                    VALUES(@team,@department,'Legacy team',true,@created);
                """, connection))
            {
                insert.Parameters.AddWithValue("department", department);
                insert.Parameters.AddWithValue("user", user);
                insert.Parameters.AddWithValue("team", team);
                insert.Parameters.AddWithValue("created", DateTime.UtcNow);
                await insert.ExecuteNonQueryAsync();
            }
            if (scenario != "valid")
            {
                await using var invalid = new NpgsqlCommand("""
                    INSERT INTO organization.departments(id,name,is_active,created_at) VALUES(@id,@name,true,@created)
                    """, connection);
                invalid.Parameters.AddWithValue("id", Guid.NewGuid());
                invalid.Parameters.AddWithValue("name", scenario == "blank" ? "   " : "legacy finance");
                invalid.Parameters.AddWithValue("created", DateTime.UtcNow);
                await invalid.ExecuteNonQueryAsync();
                var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
                Assert.Equal("P0001", error.SqlState);
                Assert.DoesNotContain("Legacy Finance", error.MessageText);
                Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
            }
            else
            {
                // Exercise the reviewed SQL with real legacy rows, then rollback/reapply.
                await using var upgrade = new NpgsqlCommand(ModelTests.ReadSql("DepartmentAdministration.sql"), connection);
                await upgrade.ExecuteNonQueryAsync();
                await upgrade.ExecuteNonQueryAsync();
                var migrated = await context.Departments.AsNoTracking().SingleAsync(x => x.Id == department);
                Assert.Equal(" Legacy Finance ", migrated.Name);
                Assert.Equal("legacy finance", migrated.NormalizedName);
                Assert.Equal("Historical description", migrated.Description);
                Assert.False(migrated.IsActive);
                Assert.True(migrated.Version > 0);
                await using var rollback = new NpgsqlCommand(ModelTests.ReadSql("DepartmentAdministration.Rollback.sql"), connection);
                await rollback.ExecuteNonQueryAsync();
                Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
                await upgrade.ExecuteNonQueryAsync();
                Assert.Contains("20261026143539_DepartmentAdministration", await context.Database.GetAppliedMigrationsAsync());
            }
            Assert.Equal(department, (await context.Users.SingleAsync(x => x.Id == user)).DepartmentId);
            var legacyTeam = await context.SupportTeams.Where(x => x.Id == team)
                .Select(x => new { x.DepartmentId, x.IsActive }).SingleAsync();
            Assert.Equal(department, legacyTeam.DepartmentId);
            Assert.True(legacyTeam.IsActive);
        }
        finally
        {
            if (!databaseName.StartsWith("helpdesk_mapping_test_departments_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the generated test database.");
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", administration);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
