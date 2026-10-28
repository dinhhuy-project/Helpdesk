using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class TicketCategoryMigrationTests(PostgresFixture fixture)
{
    [PostgresTheory]
    [InlineData("valid")]
    [InlineData("duplicate-root")]
    [InlineData("duplicate-child")]
    [InlineData("blank")]
    [InlineData("self")]
    [InlineData("cycle")]
    public async Task UpgradePreservesLegacyTreeAndTicketAndRefusesInvalidData(string scenario)
    {
        var databaseName = "helpdesk_mapping_test_categories_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = databaseName, Pooling = false };
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", administration))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using var context = ModelTests.CreateContext(settings.ConnectionString);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261026145925_SupportTeamAdministration");
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            var root = Guid.NewGuid();
            var child = Guid.NewGuid();
            var other = Guid.NewGuid();
            var user = Guid.NewGuid();
            var ticket = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO helpdesk.ticket_categories(id,name,is_active,created_at) VALUES(@root,' Legacy Root ',false,@created),(@other,'Other root',true,@created);
                INSERT INTO helpdesk.ticket_categories(id,parent_id,name,is_active,created_at)
                    VALUES(@child,@root,' Legacy child ',true,@created),(@otherChild,@other,'legacy child',true,@created);
                INSERT INTO identity.users(id,email,display_name,is_active,created_at) VALUES(@user,'legacy-category@example.test','Legacy user',true,@created);
                INSERT INTO helpdesk.tickets(id,title,description,requester_id,created_by_user_id,category_id,status,priority,created_at)
                    VALUES(@ticket,'Legacy ticket','History',@user,@user,@child,'New','Normal',@created);
                """, connection))
            {
                insert.Parameters.AddWithValue("root", root);
                insert.Parameters.AddWithValue("child", child);
                insert.Parameters.AddWithValue("other", other);
                insert.Parameters.AddWithValue("otherChild", Guid.NewGuid());
                insert.Parameters.AddWithValue("user", user);
                insert.Parameters.AddWithValue("ticket", ticket);
                insert.Parameters.AddWithValue("created", DateTime.UtcNow);
                await insert.ExecuteNonQueryAsync();
            }
            if (scenario is "self" or "cycle")
            {
                await using var cycle = new NpgsqlCommand("UPDATE helpdesk.ticket_categories SET parent_id=@parent WHERE id=@root", connection);
                cycle.Parameters.AddWithValue("parent", scenario == "self" ? root : child);
                cycle.Parameters.AddWithValue("root", root);
                await cycle.ExecuteNonQueryAsync();
            }
            else if (scenario != "valid")
            {
                await using var invalid = new NpgsqlCommand("""
                    INSERT INTO helpdesk.ticket_categories(id,parent_id,name,is_active,created_at) VALUES(@id,@parent,@name,true,@created)
                    """, connection);
                invalid.Parameters.AddWithValue("id", Guid.NewGuid());
                invalid.Parameters.Add(new NpgsqlParameter("parent", NpgsqlTypes.NpgsqlDbType.Uuid)
                    { Value = scenario == "duplicate-child" ? root : DBNull.Value });
                invalid.Parameters.AddWithValue("name", scenario == "blank" ? "   " : scenario == "duplicate-child" ? "LEGACY CHILD" : "legacy root");
                invalid.Parameters.AddWithValue("created", DateTime.UtcNow);
                await invalid.ExecuteNonQueryAsync();
            }
            if (scenario != "valid")
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
                Assert.Equal("P0001", error.SqlState);
                Assert.DoesNotContain("Legacy Root", error.MessageText);
                Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count());
            }
            else
            {
                await using var upgrade = new NpgsqlCommand(ModelTests.ReadSql("TicketCategoryAdministration.sql"), connection);
                await upgrade.ExecuteNonQueryAsync();
                await upgrade.ExecuteNonQueryAsync();
                var migrated = await context.TicketCategories.AsNoTracking().SingleAsync(x => x.Id == root);
                Assert.Equal(" Legacy Root ", migrated.Name);
                Assert.Equal("legacy root", migrated.NormalizedName);
                Assert.False(migrated.IsActive);
                Assert.True(migrated.Version > 0);
                await using var rollback = new NpgsqlCommand(ModelTests.ReadSql("TicketCategoryAdministration.Rollback.sql"), connection);
                await rollback.ExecuteNonQueryAsync();
                Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count());
                await upgrade.ExecuteNonQueryAsync();
                Assert.Contains("20261026151829_TicketCategoryAdministration", await context.Database.GetAppliedMigrationsAsync());
                Assert.DoesNotContain("20261026151829_TicketCategoryAdministration", await context.Database.GetPendingMigrationsAsync());
            }
            var legacy = await context.TicketCategories.Where(x => x.Id == child)
                .Select(x => new { x.Name, x.ParentId, x.IsActive }).SingleAsync();
            Assert.Equal(" Legacy child ", legacy.Name);
            Assert.Equal(root, legacy.ParentId);
            Assert.True(legacy.IsActive);
            Assert.Equal(child, (await context.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticket)).CategoryId);
        }
        finally
        {
            if (!databaseName.StartsWith("helpdesk_mapping_test_categories_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the generated test database.");
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", administration);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
