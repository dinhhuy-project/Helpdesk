using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using be.Application.Features.Accounts;
using be.Application.Features.Community;
using be.Domain.Entities;
using be.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class KnowledgeGuideRulesTests
{
    [Theory]
    [InlineData("1. Check the connection.", true)]
    [InlineData("1. Check the connection.\n2) Record the error code.", true)]
    [InlineData("1. Step one.\nAdditional details.\n2. Step two.", true)]
    [InlineData("", false)]
    [InlineData("Freeform content", false)]
    [InlineData("Intro\n1. Check", false)]
    [InlineData("2. Starts at two", false)]
    [InlineData("1. First\n3. Missing second", false)]
    [InlineData("1. First\n1. Repeated", false)]
    [InlineData("1. \n2. Empty first", false)]
    [InlineData("1. First\n0. Invalid", false)]
    [InlineData("1. First\n100. Invalid", false)]
    [InlineData("1. First\n2.No separator", false)]
    [InlineData("1. First\n2.", false)]
    [InlineData("01. Leading zero", false)]
    public void StepsMustBeNonemptyConsecutiveAndStartAtOne(string content, bool expected) => Assert.Equal(expected, KnowledgeGuideRules.NumberedSteps(content));
    [Fact]
    public void OversizedOrTooManyStepsAreRejected()
    {
        Assert.False(KnowledgeGuideRules.NumberedSteps("1. " + new string('x', 4001)));
        Assert.False(KnowledgeGuideRules.NumberedSteps(string.Join('\n', Enumerable.Range(1, 21).Select(x => x + ". Step"))));
    }
    [Fact]
    public void CompletenessIncludesScopePrerequisitesOutcomeEscalationAndAttribution()
    {
        var complete = CommunityTests.Guide();
        Assert.True(KnowledgeGuideRules.Complete(complete, "1. Check"));
        foreach (var guide in new[] { complete with { Applicability = "" }, complete with { Prerequisites = " " }, complete with { ExpectedOutcome = "" },
                     complete with { Escalation = "" }, complete with { SourceName = "" } }) Assert.False(KnowledgeGuideRules.Complete(guide, "1. Check"));
        Assert.False(KnowledgeGuideRules.Complete(null, "1. Check"));
    }
    [Theory]
    [InlineData("http://wiki.example.test/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.test/")]
    public void PublicSourceLinksRequireHttpsWithoutCredentials(string url) =>
        Assert.Throws<CommunityOperationException>(() => KnowledgeGuideRules.Normalize(CommunityTests.Guide() with { SourceUrl = url }, new DateOnly(2026, 10, 31)));
    [Fact]
    public void SourceDatesAreOptionalButCannotBeInventedInTheFuture()
    {
        var today = new DateOnly(2026, 10, 31);
        Assert.Null(KnowledgeGuideRules.Normalize(CommunityTests.Guide(), today)!.SourceUpdatedOn);
        Assert.Throws<CommunityOperationException>(() => KnowledgeGuideRules.Normalize(CommunityTests.Guide() with { SourceUpdatedOn = today.AddDays(1) }, today));
    }
}

[Collection("Postgres")]
public class KnowledgeStandardizationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task DatabaseRejectsUnknownClassificationsAndNonObjectGuideMetadata()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        var row = await CommunityTests.CreateAsync(admin, "knowledge");
        await using var database = test.Database();
        var invalidClass = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE helpdesk.community_entries SET classification = 'UnknownValue' WHERE id = {row.Id}"));
        Assert.Equal("23514", invalidClass.SqlState);
        var invalidGuide = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE helpdesk.community_entries SET guide = '[]'::jsonb WHERE id = {row.Id}"));
        Assert.Equal("23514", invalidGuide.SqlState);
    }
    [PostgresFact]
    public async Task ReferenceDraftsAreCompleteUnpublishedAndRerunsPreserveEditorChanges()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        await using var database = test.Database();
        var seed = new KnowledgeDraftInitializer(database, TimeProvider.System);
        Assert.True(await seed.SeedAsync());
        var rows = await database.CommunityEntries.ToListAsync();
        Assert.Equal(8, rows.Count);
        Assert.All(rows, row => { Assert.False(row.IsPublished); Assert.Equal(CommunityClassification.Unclassified, row.Classification); Assert.True(KnowledgeGuideRules.Complete(row.Guide, row.Content)); });
        rows[0].Title = "An editor has updated the candidate";
        await database.SaveChangesAsync();
        Assert.False(await seed.SeedAsync());
        Assert.Equal("An editor has updated the candidate", (await database.CommunityEntries.FindAsync(rows[0].Id))!.Title);
        using var guest = test.Factory.CreateClient();
        Assert.Equal(0, (await guest.GetFromJsonAsync<PagedResult<PublicCommunityEntry>>("/api/v1/public/knowledge"))!.TotalCount);
    }
    [PostgresFact]
    public async Task RollbackDoesNotExposeDemoContentThroughAnOlderApiWithoutDatasetFilters()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        var operational = await CommunityTests.PublishAsync(admin, "knowledge", await CommunityTests.CreateAsync(admin, "knowledge"));
        using var create = await admin.PostAsJsonAsync("/api/v1/community/incidents", new SaveCommunityEntry("Demo incident", "Example", "Example", "Example", "Investigating", Classification: "Demo"));
        var demo = await CommunityTests.PublishAsync(admin, "incidents", (await create.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!);
        await using var database = test.Database();
        await database.GetService<IMigrator>().MigrateAsync("PublicCommunity");
        await using var connection = new NpgsqlConnection(test.ConnectionString);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT is_published FROM helpdesk.community_entries WHERE id = @id", connection);
        query.Parameters.AddWithValue("id", demo.Id);
        Assert.False((bool)(await query.ExecuteScalarAsync())!);
        query.Parameters[0].Value = operational.Id;
        Assert.True((bool)(await query.ExecuteScalarAsync())!);
    }
    [PostgresFact]
    public async Task DraftsCanBeIncompleteButPublishRequiresClassificationAndACompleteGuide()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var guest = test.Factory.CreateClient();
        using var created = await admin.PostAsJsonAsync("/api/v1/community/knowledge", new SaveCommunityEntry("Draft", "Summary", "Imported freeform content", "VPN"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
        using var unclassified = await admin.PostAsJsonAsync($"/api/v1/community/knowledge/{row.Id}/publish", new PublishCommunityEntry(row.Version, true));
        Assert.Equal(HttpStatusCode.BadRequest, unclassified.StatusCode);
        Assert.Equal("ClassificationRequired", (await unclassified.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
        using var save = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{row.Id}", new SaveCommunityEntry(row.Title, row.Summary, row.Content, row.Topic,
            Version: row.Version, Classification: "Operational", Guide: new("", "", "", "", "")));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        row = (await save.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
        using var incomplete = await admin.PostAsJsonAsync($"/api/v1/community/knowledge/{row.Id}/publish", new PublishCommunityEntry(row.Version, true));
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/knowledge/{row.Id}")).StatusCode);
        using var ready = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{row.Id}", new SaveCommunityEntry(row.Title, row.Summary, "1. Check the approved client.\n2. Record the error code.", row.Topic,
            Version: row.Version, Classification: "Operational", Guide: CommunityTests.Guide()));
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        row = await CommunityTests.PublishAsync(admin, "knowledge", (await ready.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!);
        var published = await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/knowledge/{row.Id}");
        Assert.Equal("Operational", published!.Classification); Assert.Equal(CommunityTests.Guide(), published.Guide);
        Assert.True(published.PublishedAt <= DateTime.UtcNow);
    }

    [PostgresFact]
    public async Task DemoListDetailSearchAndIncidentTimelineStayOutOfTheOperationalDataset()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var guest = test.Factory.CreateClient();
        var operational = await CommunityTests.PublishAsync(admin, "knowledge", await CommunityTests.CreateAsync(admin, "knowledge"));
        using var create = await admin.PostAsJsonAsync("/api/v1/community/knowledge", new SaveCommunityEntry("Demo-only marker", "Example", "1. Demonstration", "Example", Classification: "Demo", Guide: CommunityTests.Guide()));
        var demo = await CommunityTests.PublishAsync(admin, "knowledge", (await create.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!);
        var normal = await guest.GetFromJsonAsync<PagedResult<PublicCommunityEntry>>("/api/v1/public/knowledge");
        Assert.Equal(operational.Id, Assert.Single(normal!.Items).Id); Assert.Equal(1, normal.TotalCount);
        var examples = await guest.GetFromJsonAsync<PagedResult<PublicCommunityEntry>>("/api/v1/public/knowledge?dataset=demo");
        Assert.Equal(demo.Id, Assert.Single(examples!.Items).Id); Assert.EndsWith("?dataset=demo", examples.Items[0].SourceUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/knowledge/{demo.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/knowledge/{operational.Id}?dataset=demo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/v1/public/knowledge/{demo.Id}?dataset=demo")).StatusCode);
        Assert.Equal(0, (await guest.GetFromJsonAsync<PagedResult<PublicCommunityEntry>>("/api/v1/public/knowledge?search=Demo-only"))!.TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/v1/public/knowledge?dataset=all")).StatusCode);
        using var incidentCreate = await admin.PostAsJsonAsync("/api/v1/community/incidents", new SaveCommunityEntry("Example incident", "Demo", "Example", "Demo", "Investigating", Classification: "Demo"));
        var incident = await CommunityTests.PublishAsync(admin, "incidents", (await incidentCreate.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/incidents/{incident.Id}/updates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/v1/public/incidents/{incident.Id}/updates?dataset=demo")).StatusCode);
    }

    [PostgresFact]
    public async Task EditingMetadataOrClassificationDoesNotChangeThePublishedSnapshotUntilReviewed()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var guest = test.Factory.CreateClient();
        var row = await CommunityTests.PublishAsync(admin, "knowledge", await CommunityTests.CreateAsync(admin, "knowledge"));
        using var edit = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{row.Id}", new SaveCommunityEntry(row.Title, row.Summary, row.Content, row.Topic,
            Version: row.Version, Classification: "Demo", Guide: row.Guide! with { Applicability = "private-scope-marker", SourceName = "private-source-marker" }));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var current = await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/knowledge/{row.Id}");
        Assert.Equal("Operational", current!.Classification); Assert.Equal(CommunityTests.Guide(), current.Guide);
        using var stale = await admin.PostAsJsonAsync($"/api/v1/community/knowledge/{row.Id}/publish", new PublishCommunityEntry(row.Version, true));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var edited = (await edit.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
        await CommunityTests.PublishAsync(admin, "knowledge", edited);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/knowledge/{row.Id}")).StatusCode);
        var demo = await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/knowledge/{row.Id}?dataset=demo");
        Assert.Equal("private-scope-marker", demo!.Guide!.Applicability);
    }

    [PostgresFact]
    public async Task MigrationPreservesExistingContentAndOnlyRecognizesKnownSeedIdsAsDemo()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        var demoId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("community-demo-v1:article:0"))[..16]);
        var customId = Guid.NewGuid();
        await using (var database = test.Database())
        {
            await database.GetService<IMigrator>().MigrateAsync("PublicCommunity");
            foreach (var id in new[] { demoId, customId })
                await database.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO helpdesk.community_entries
                        (id, kind, title, summary, content, topic, created_by_id, created_at, updated_at,
                         is_published, public_title, public_summary, public_content, public_topic, public_revision, published_at)
                    VALUES ({id}, 'Knowledge', 'Old draft', 'Old summary', 'Original draft stays', 'VPN', {test.AdminId}, now(), now(),
                        true, 'Old public title', 'Old public summary', 'Original published body stays', 'VPN', 3, now());
                    """);
            await database.Database.MigrateAsync();
            var custom = await database.CommunityEntries.AsNoTracking().SingleAsync(x => x.Id == customId);
            var demo = await database.CommunityEntries.AsNoTracking().SingleAsync(x => x.Id == demoId);
            Assert.Equal(CommunityClassification.Unclassified, custom.PublicClassification); Assert.True(custom.IsPublished);
            Assert.Equal("Original draft stays", custom.Content); Assert.Equal("Original published body stays", custom.PublicContent); Assert.Equal(3, custom.PublicRevision);
            Assert.Equal(CommunityClassification.Demo, demo.PublicClassification); Assert.NotNull(demo.PublicGuide);
            Assert.Equal("Original published body stays", demo.PublicContent);
        }
        using var guest = test.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/knowledge/{customId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/knowledge/{demoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/v1/public/knowledge/{demoId}?dataset=demo")).StatusCode);
    }
}
