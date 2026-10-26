using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class ModelTests
{
    [Fact]
    public void ModelHasExplicitSchemasKeysAndNoShadowForeignKeys()
    {
        using var context = CreateContext();
        var entities = context.Model.GetEntityTypes().ToArray();
        Assert.Equal(19, entities.Length);
        Assert.Equal(new[] { "communication", "helpdesk", "identity", "organization" },
            entities.Select(x => x.GetSchema()).Distinct().OrderBy(x => x));
        foreach (var entity in entities)
        {
            Assert.NotNull(entity.FindPrimaryKey());
            Assert.Matches("^[a-z_]+$", entity.GetTableName()!);
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            foreach (var property in entity.GetProperties())
            {
                Assert.False(property.IsShadowProperty());
                Assert.Matches("^[a-z_]+$", property.GetColumnName(table)!);
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTime))
                    Assert.Equal("timestamp with time zone", property.GetColumnType());
            }
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        }
    }

    [Fact]
    public void TicketUsesDatabaseIdentityAndXminWithoutCreatingASystemColumn()
    {
        using var context = CreateContext();
        var ticket = context.Model.FindEntityType(typeof(Ticket))!;
        var version = ticket.FindProperty(nameof(Ticket.Version))!;
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        Assert.Equal("xmin", version.GetColumnName());
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains("GENERATED ALWAYS AS IDENTITY", sql);
        Assert.DoesNotContain("xmin xid", sql);
        Assert.Contains("jsonb", sql);
        Assert.Contains("ck_tickets_status", sql);
    }

    [Fact]
    public void MigrationSnapshotAndReviewedSqlMatchCurrentModel()
    {
        using var context = CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var migrations = context.Database.GetMigrations().ToArray();
        Assert.Equal(5, migrations.Length);
        Assert.EndsWith("_InitialCreate", migrations[0]);
        Assert.EndsWith("_AccountAdministration", migrations[1]);
        Assert.EndsWith("_DepartmentAdministration", migrations[2]);
        Assert.EndsWith("_SupportTeamAdministration", migrations[3]);
        Assert.EndsWith("_TicketCategoryAdministration", migrations[4]);
        var migrator = context.GetService<IMigrator>();
        var forward = migrator.GenerateScript("0", migrations[0], MigrationsSqlGenerationOptions.Idempotent);
        var rollback = migrator.GenerateScript(migrations[0], "0");
        Assert.Equal(NormalizeSql(forward), NormalizeSql(ReadSql("InitialCreate.sql")));
        Assert.Equal(NormalizeSql(rollback), NormalizeSql(ReadSql("InitialCreate.Rollback.sql")));
        Assert.Equal(NormalizeSql(migrator.GenerateScript(migrations[0], migrations[1], MigrationsSqlGenerationOptions.Idempotent)),
            NormalizeSql(ReadSql("AccountAdministration.sql")));
        Assert.Equal(NormalizeSql(migrator.GenerateScript(migrations[1], migrations[0])),
            NormalizeSql(ReadSql("AccountAdministration.Rollback.sql")));
        Assert.Equal(NormalizeSql(migrator.GenerateScript(migrations[1], migrations[2], MigrationsSqlGenerationOptions.Idempotent)),
            NormalizeSql(ReadSql("DepartmentAdministration.sql")));
        Assert.Equal(NormalizeSql(migrator.GenerateScript(migrations[2], migrations[1])),
            NormalizeSql(ReadSql("DepartmentAdministration.Rollback.sql")));
        Assert.Equal(NormalizeSql(migrator.GenerateScript(migrations[2], migrations[3], MigrationsSqlGenerationOptions.Idempotent)),
            NormalizeSql(ReadSql("SupportTeamAdministration.sql")));
        Assert.Equal(NormalizeSql(migrator.GenerateScript(migrations[3], migrations[2])),
            NormalizeSql(ReadSql("SupportTeamAdministration.Rollback.sql")));
        Assert.Equal(NormalizeSql(migrator.GenerateScript(migrations[3], migrations[4], MigrationsSqlGenerationOptions.Idempotent)),
            NormalizeSql(ReadSql("TicketCategoryAdministration.sql")));
        Assert.Equal(NormalizeSql(migrator.GenerateScript(migrations[4], migrations[3])),
            NormalizeSql(ReadSql("TicketCategoryAdministration.Rollback.sql")));
    }

    internal static string ReadSql(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", fileName));

    private static string NormalizeSql(string sql) => sql.Replace("\r\n", "\n").Trim();

    internal static HelpdeskDbContext CreateContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseNpgsql(connectionString ?? "Host=localhost;Database=unused;Username=unused",
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
            .Options);
}
