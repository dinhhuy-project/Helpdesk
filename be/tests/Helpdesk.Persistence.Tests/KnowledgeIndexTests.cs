using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Community;
using be.Infrastructure.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class KnowledgeIndexManifestTests
{
    private static KnowledgeIndexSnapshot Snapshot(params KnowledgeIndexDocument[] documents) =>
        new(1, "lexical-v1", "manifest-test", DateTime.UtcNow, KnowledgeIndexMaintenance.IndexName, true, documents);
    private static KnowledgeIndexDocument Document(Guid id, int revision = 1, char hash = 'a') => new(id, revision, new string(hash, 64));

    [Fact]
    public async Task CheckpointDetectsChangesAndRepeatedRunsAreIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), "knowledge-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var third = Guid.NewGuid();
        try
        {
            await using (var manifest = await KnowledgeIndexManifest.OpenAsync(path, default))
                await manifest.SaveAsync(Snapshot(Document(first), Document(second)), default);
            var next = Snapshot(Document(first, 2, 'b'), Document(third));
            await using (var manifest = await KnowledgeIndexManifest.OpenAsync(path, default))
            {
                var delta = manifest.Compare(next);
                Assert.Equal(new[] { third }, delta.Added); Assert.Equal(new[] { first }, delta.Updated); Assert.Equal(new[] { second }, delta.Removed);
                await manifest.SaveAsync(next, default);
            }
            await using (var manifest = await KnowledgeIndexManifest.OpenAsync(path, default))
            {
                var delta = manifest.Compare(next); Assert.Empty(delta.Added); Assert.Empty(delta.Updated); Assert.Empty(delta.Removed);
            }
            var json = await File.ReadAllTextAsync(path); Assert.DoesNotContain("content", json); Assert.DoesNotContain("password", json);
        }
        finally { File.Delete(path); File.Delete(path + ".lock"); }
    }

    [Theory]
    [InlineData("unhealthy")]
    [InlineData("unversioned")]
    [InlineData("regression")]
    [InlineData("other-database")]
    [InlineData("cancelled")]
    public async Task FailedRunPreservesTheLastCheckpoint(string failure)
    {
        var path = Path.Combine(Path.GetTempPath(), "knowledge-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        var id = Guid.NewGuid(); var original = Snapshot(Document(id, 2));
        try
        {
            await using var manifest = await KnowledgeIndexManifest.OpenAsync(path, default);
            await manifest.SaveAsync(original, default);
            var bytes = await File.ReadAllBytesAsync(path);
            // Reopen to compare with the durable baseline rather than the initial empty snapshot.
            await manifest.DisposeAsync();
            await using var retry = await KnowledgeIndexManifest.OpenAsync(path, default);
            var next = failure switch
            {
                "unhealthy" => original with { Healthy = false },
                "unversioned" => Snapshot(Document(id, 2, 'b')),
                "regression" => Snapshot(Document(id, 1)),
                "other-database" => original with { DatabaseName = "another-database" },
                _ => original
            };
            using var cancelled = new CancellationTokenSource(); if (failure == "cancelled") cancelled.Cancel();
            await Assert.ThrowsAnyAsync<Exception>(() => retry.SaveAsync(next, cancelled.Token));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
        finally { File.Delete(path); File.Delete(path + ".lock"); }
    }

    [Fact]
    public async Task CompetingOrCorruptManifestsCannotOverwriteTheBaseline()
    {
        var path = Path.Combine(Path.GetTempPath(), "knowledge-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await using (var manifest = await KnowledgeIndexManifest.OpenAsync(path, default))
                await Assert.ThrowsAsync<IOException>(() => KnowledgeIndexManifest.OpenAsync(path, default));
            await File.WriteAllTextAsync(path, "{not-a-manifest");
            await Assert.ThrowsAsync<JsonException>(() => KnowledgeIndexManifest.OpenAsync(path, default));
            Assert.Equal("{not-a-manifest", await File.ReadAllTextAsync(path));
            // Failed parsing must release the file lock too.
            await using var gate = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); File.Delete(path + ".lock"); }
    }
}

[Collection("Postgres")]
public class KnowledgeIndexTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task PublicationTracksTheSameIdWithNewRevisionsAndWithdrawnDocumentsDisappear()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var guest = test.Factory.CreateClient();
        var published = await CommunityTests.PublishAsync(admin, "knowledge", await CommunityTests.CreateAsync(admin, "knowledge"));
        await using var database = test.Database();
        var maintenance = new KnowledgeIndexMaintenance(database, TimeProvider.System);
        var first = await maintenance.InspectAsync(false, default);
        Assert.True(first.Healthy); var original = Assert.Single(first.Documents); Assert.Equal(published.Id, original.EntryId);
        var edited = await EditAsync(admin, published, "1. Syncnewneedle approved procedure.");
        Assert.Equal(first.Documents, (await maintenance.InspectAsync(false, default)).Documents);
        var republished = await CommunityTests.PublishAsync(admin, "knowledge", edited);
        var updated = Assert.Single((await maintenance.InspectAsync(false, default)).Documents);
        Assert.Equal(original.EntryId, updated.EntryId); Assert.Equal(original.Revision + 1, updated.Revision); Assert.NotEqual(original.Fingerprint, updated.Fingerprint);
        var result = await guest.GetFromJsonAsync<KnowledgeRetrievalResult>("/api/v1/public/knowledge/retrieve?query=Syncnewneedle");
        Assert.Equal(republished.PublicRevision, Assert.Single(result!.Items).Revision);
        using var withdraw = await admin.PostAsJsonAsync($"/api/v1/community/knowledge/{published.Id}/withdraw", new { republished.Version });
        Assert.Equal(HttpStatusCode.OK, withdraw.StatusCode);
        Assert.Empty((await maintenance.InspectAsync(false, default)).Documents);
        Assert.Empty((await guest.GetFromJsonAsync<KnowledgeRetrievalResult>("/api/v1/public/knowledge/retrieve?query=Syncnewneedle"))!.Items);
        var retained = await database.CommunityEntries.AsNoTracking().SingleAsync(x => x.Id == published.Id);
        Assert.Equal(updated.Revision, retained.PublicRevision); Assert.Contains("Syncnewneedle", retained.PublicContent);
        var withdrawn = (await withdraw.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
        var restored = await CommunityTests.PublishAsync(admin, "knowledge", withdrawn);
        Assert.Equal(updated.Revision + 1, Assert.Single((await maintenance.InspectAsync(false, default)).Documents).Revision);
        var demo = await EditAsync(admin, restored, restored.Content, "Demo");
        await CommunityTests.PublishAsync(admin, "knowledge", demo);
        Assert.Empty((await maintenance.InspectAsync(false, default)).Documents);
    }

    [PostgresFact]
    public async Task NativeIndexAndManifestKeepTheCommittedRevisionWhenPublicationRollsBack()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        var row = await CommunityTests.PublishAsync(admin, "knowledge", await CommunityTests.CreateAsync(admin, "knowledge"));
        await using var observer = test.Database(); var maintenance = new KnowledgeIndexMaintenance(observer, TimeProvider.System);
        var baseline = await maintenance.InspectAsync(false, default);
        await using (var writer = test.Database())
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            await writer.CommunityEntries.Where(x => x.Id == row.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PublicContent, "1. Failedpublicationneedle should never be visible.")
                .SetProperty(x => x.PublicRevision, x => x.PublicRevision + 1));
            Assert.Equal(baseline.Documents, (await maintenance.InspectAsync(false, default)).Documents);
            await transaction.RollbackAsync();
        }
        await observer.Database.OpenConnectionAsync();
        try
        {
            await observer.Database.ExecuteSqlRawAsync("SET enable_seqscan=off;");
            var store = new KnowledgeRetrievalStore(observer);
            Assert.Empty(await store.CandidatesAsync("failedpublicationneedle", default));
            Assert.Equal(row.Id, Assert.Single(await store.CandidatesAsync("vpn", default)).Id);
        }
        finally { await observer.Database.CloseConnectionAsync(); }
        Assert.Equal(baseline.Documents, (await maintenance.InspectAsync(false, default)).Documents);
    }

    [PostgresFact]
    public async Task RebuildCanBeRepeatedWithoutChangingArticlesRevisionsOrAudit()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        var row = await CommunityTests.PublishAsync(admin, "knowledge", await CommunityTests.CreateAsync(admin, "knowledge"));
        await using var database = test.Database(); var maintenance = new KnowledgeIndexMaintenance(database, TimeProvider.System);
        var original = await database.CommunityEntries.AsNoTracking().SingleAsync(x => x.Id == row.Id);
        var auditCount = await database.IdentityAuditLogs.CountAsync();
        var first = await maintenance.InspectAsync(true, default); var retry = await maintenance.InspectAsync(true, default);
        Assert.True(first.Healthy); Assert.True(retry.Healthy); Assert.Equal(first.Documents, retry.Documents);
        var after = await database.CommunityEntries.AsNoTracking().SingleAsync(x => x.Id == row.Id);
        Assert.Equal(original.Version, after.Version); Assert.Equal(original.PublicRevision, after.PublicRevision); Assert.Equal(original.PublicContent, after.PublicContent);
        Assert.Equal(auditCount, await database.IdentityAuditLogs.CountAsync());
    }

    [PostgresFact]
    public async Task CliKeepsCheckpointOnLockFailureAndCanRerunAfterWriterReleasesItsLock()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        var row = await CommunityTests.PublishAsync(admin, "knowledge", await CommunityTests.CreateAsync(admin, "knowledge"));
        var path = Path.Combine(Path.GetTempPath(), "knowledge-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        var services = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System)
            .AddScoped(_ => test.Database()).AddScoped<KnowledgeIndexMaintenance>();
        await using var provider = services.BuildServiceProvider();
        try
        {
            Assert.Equal(0, await KnowledgeIndexCommands.ExecuteAsync(["knowledge-index", "check", "--manifest", path], provider));
            var baseline = await File.ReadAllBytesAsync(path);
            await using (var writer = test.Database())
            await using (var transaction = await writer.Database.BeginTransactionAsync())
            {
                await writer.CommunityEntries.Where(x => x.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, "Uncommitted draft"));
                Assert.Equal(1, await KnowledgeIndexCommands.ExecuteAsync(["knowledge-index", "rebuild", "--manifest", path], provider));
                Assert.Equal(baseline, await File.ReadAllBytesAsync(path));
                await transaction.RollbackAsync();
            }
            Assert.Equal(0, await KnowledgeIndexCommands.ExecuteAsync(["knowledge-index", "rebuild", "--manifest", path], provider));
            Assert.Equal(0, await KnowledgeIndexCommands.ExecuteAsync(["knowledge-index", "check", "--manifest", path], provider));
            Assert.Equal(2, await KnowledgeIndexCommands.ExecuteAsync(["knowledge-index", "unsupported", "--manifest", path], provider));
        }
        finally { File.Delete(path); File.Delete(path + ".lock"); }
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrDriftedIndexFailsClosedWithoutOverwritingTheReviewedSchema(bool wrongDefinition)
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        await using var database = test.Database();
        await database.Database.ExecuteSqlRawAsync("DROP INDEX helpdesk.ix_community_knowledge_retrieval;");
        if (wrongDefinition) await database.Database.ExecuteSqlRawAsync("CREATE INDEX ix_community_knowledge_retrieval ON helpdesk.community_entries (id);");
        var maintenance = new KnowledgeIndexMaintenance(database, TimeProvider.System);
        Assert.False((await maintenance.InspectAsync(false, default)).Healthy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => maintenance.InspectAsync(true, default));
    }

    private static async Task<ManagedCommunityEntry> EditAsync(HttpClient admin, ManagedCommunityEntry row, string content, string classification = "Operational")
    {
        using var response = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{row.Id}",
            new SaveCommunityEntry(row.Title, row.Summary, content, row.Topic, Version: row.Version, Classification: classification, Guide: row.Guide));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
    }
}
