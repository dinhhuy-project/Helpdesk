using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION")))
            Skip = "Set HELPDESK_TEST_CONNECTION to an isolated helpdesk_mapping_test_* database.";
    }
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION")))
            Skip = "Set HELPDESK_TEST_CONNECTION to an isolated helpdesk_mapping_test_* database.";
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    public string ConnectionString => Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION")
        ?? throw new InvalidOperationException("Missing HELPDESK_TEST_CONNECTION.");

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION")))
            return;

        var settings = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (settings.Database is null || !settings.Database.StartsWith("helpdesk_mapping_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Tests require a separate helpdesk_mapping_test_* database.");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var countCommand = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema')", connection);
        if (Convert.ToInt64(await countCommand.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("Tests require an empty database. Existing data will not be modified.");

        await using var context = ModelTests.CreateContext(ConnectionString);
        // Apply the migration API, validate the rollback artifact on our empty test schema,
        // then reapply the reviewed SQL. No application data exists at this point.
        await context.Database.MigrateAsync();
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        await using var rollbackCommand = new NpgsqlCommand(ModelTests.ReadSql("InitialCreate.Rollback.sql"), connection);
        await rollbackCommand.ExecuteNonQueryAsync();
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1L, Convert.ToInt64(await countCommand.ExecuteScalarAsync())); // history table remains
        await using var createCommand = new NpgsqlCommand(ModelTests.ReadSql("InitialCreate.sql"), connection);
        await createCommand.ExecuteNonQueryAsync();
        await context.Database.MigrateAsync(); // already applied: must be a no-op
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
