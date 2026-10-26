using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class AccountMigrationTests(PostgresFixture fixture)
{
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradePreservesLegacyEmailAndRefusesAmbiguousAccounts(bool duplicate)
    {
        // The generated name is the only database this test creates/drops, inside the disposable test server.
        var databaseName = "helpdesk_mapping_test_upgrade_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = databaseName, Pooling = false };
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", administration))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using var context = ModelTests.CreateContext(settings.ConnectionString);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261022150705_InitialCreate");
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            var first = Guid.NewGuid();
            await InsertLegacyAsync(connection, first, " Legacy@Example.Test ");
            if (duplicate) await InsertLegacyAsync(connection, Guid.NewGuid(), "legacy@example.test");
            if (duplicate)
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
                Assert.Equal("P0001", error.SqlState);
                Assert.DoesNotContain("Legacy@Example.Test", error.MessageText);
                Assert.Single(await context.Database.GetAppliedMigrationsAsync());
                await using var count = new NpgsqlCommand("SELECT count(*) FROM identity.users", connection);
                Assert.Equal(2L, Convert.ToInt64(await count.ExecuteScalarAsync()));
            }
            else
            {
                await migrator.MigrateAsync();
                var user = await context.Users.SingleAsync(x => x.Id == first);
                Assert.Equal(" Legacy@Example.Test ", user.Email);
                Assert.Equal("legacy@example.test", user.NormalizedEmail);
                Assert.True(user.Version > 0);
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            if (!databaseName.StartsWith("helpdesk_mapping_test_upgrade_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the generated test database.");
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", administration);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertLegacyAsync(NpgsqlConnection connection, Guid id, string email)
    {
        await using var insert = new NpgsqlCommand(
            "INSERT INTO identity.users(id,email,display_name,is_active,created_at) VALUES(@id,@email,'Legacy',true,@created)", connection);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("email", email);
        insert.Parameters.AddWithValue("created", DateTime.UtcNow);
        await insert.ExecuteNonQueryAsync();
    }
}
