using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using be.Application.Features.Community;
using be.Domain.Entities;
using be.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Helpdesk.Persistence.Tests;

public class KnowledgeRetrievalTextTests
{
    [Theory]
    [InlineData("ĐỔI MẬT KHẨU!", "doi mat khau")]
    [InlineData("Tệp đính kèm", "tep dinh kem")]
    [InlineData("  VPN / OTP; ' OR 1=1 --", "vpn otp or 1 1")]
    public void AccentNormalizationAlsoHandlesDecomposedVietnameseAndDStroke(string input, string expected) => Assert.Equal(expected, KnowledgeRetrievalText.Normalize(input));
    [Theory]
    [InlineData("Làm sao đổi mật khẩu?", "change <-> password")]
    [InlineData("toi muon gui file", "upload")]
    [InlineData("mạng riêng ảo", "vpn")]
    [InlineData("2FA", "mfa")]
    public void ReviewedAliasesExpandWithoutAcceptingQueryOperators(string query, string expansion) =>
        Assert.Contains(expansion, KnowledgeRetrievalText.TsQuery(KnowledgeRetrievalText.Groups(query)));
    [Fact]
    public void TokenMatchingCannotConfuseAcronymsWithSubstrings()
    {
        Assert.False(KnowledgeRetrievalText.Matches("openvpn", ["vpn"]));
        Assert.True(KnowledgeRetrievalText.Matches("ket noi vpn", ["vpn"]));
    }
    [Fact]
    public void RepeatingAnAcronymDoesNotRequireItsLiteralSpellingInTheSource() =>
        Assert.Single(KnowledgeRetrievalText.Groups("mk mk"));
    [Fact]
    public async Task CorruptOrIncompleteSnapshotsAreSkippedWithoutFabricatingAnExcerpt()
    {
        var store = new FixedCandidates(new KnowledgeCandidate(Guid.NewGuid(), "VPN", "VPN", "1. VPN check.", "Network", 1, DateTime.UtcNow,
            "{\"applicability\":17,\"sourceUpdatedOn\":\"not-a-date\"}"),
            new KnowledgeCandidate(Guid.NewGuid(), "VPN", "VPN", "1. VPN check.", "Network", 1, DateTime.UtcNow, "{}"));
        Assert.Empty((await new KnowledgeRetrievalService(store).SearchAsync(new("VPN"), default)).Items);
    }
    [Fact]
    public async Task EmptyGenericQuestionsDoNotScanTheCorpus()
    {
        var store = new NeverCalled();
        Assert.Empty((await new KnowledgeRetrievalService(store).SearchAsync(new("lam sao vay"), default)).Items);
        foreach (var request in new[] { new KnowledgeRetrievalRequest("x"), new("VPN", 9), new("VPN", 0), new(new string('x', 201)), new("VPN\nOTP"), new("\ud800x") })
            await Assert.ThrowsAsync<CommunityOperationException>(() => new KnowledgeRetrievalService(store).SearchAsync(request, default));
    }
    private sealed class NeverCalled : IKnowledgeRetrievalStore
    {
        public Task<IReadOnlyList<KnowledgeCandidate>> CandidatesAsync(string tsQuery, CancellationToken ct) => throw new InvalidOperationException("Must not query the corpus.");
    }
    private sealed class FixedCandidates(params KnowledgeCandidate[] rows) : IKnowledgeRetrievalStore
    {
        public Task<IReadOnlyList<KnowledgeCandidate>> CandidatesAsync(string tsQuery, CancellationToken ct) => Task.FromResult<IReadOnlyList<KnowledgeCandidate>>(rows);
    }
}

[Collection("Postgres")]
public class KnowledgeRetrievalTests(PostgresFixture fixture, ITestOutputHelper output)
{
    private const string Endpoint = "/api/v1/public/knowledge/retrieve?query=";
    private static async Task<KnowledgeRetrievalResult> SearchAsync(HttpClient client, string query, int limit = 5) =>
        (await client.GetFromJsonAsync<KnowledgeRetrievalResult>(Endpoint + Uri.EscapeDataString(query) + "&limit=" + limit))!;

    [PostgresFact]
    public async Task EvaluateAccentsAcronymsParaphrasesAndNegativeQueriesAgainstReviewedPassages()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var guest = test.Factory.CreateClient();
        await using var database = test.Database();
        await new KnowledgeDraftInitializer(database, TimeProvider.System).SeedAsync();
        foreach (var row in await database.CommunityEntries.AsNoTracking().ToListAsync())
        {
            using var save = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{row.Id}",
                new SaveCommunityEntry(row.Title, row.Summary, row.Content, row.Topic, Version: row.Version, Classification: "Operational", Guide: row.Guide));
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            await CommunityTests.PublishAsync(admin, "knowledge", (await save.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!);
        }
        var vpn = await AddGuideAsync(admin, "Kết nối mạng riêng ảo VPN", "Mạng", "1. Mở ứng dụng VPN đã được đơn vị phê duyệt.\n2. Kiểm tra kết nối Internet rồi chọn Kết nối VPN.\n3. Ghi lại mã lỗi nếu kết nối VPN thất bại.");
        var mfa = await AddGuideAsync(admin, "Xác thực hai lớp MFA", "Bảo mật", "1. Mở ứng dụng xác thực hai lớp đã đăng ký.\n2. Nhập mã OTP vào màn hình đăng nhập chính thức.\n3. Không chia sẻ mã OTP cho người khác.");
        var printer = await AddGuideAsync(admin, "Chọn máy in và gửi lệnh in", "Thiết bị", "1. Kiểm tra máy in đang bật và có giấy.\n2. Chọn đúng máy in trong danh sách thiết bị được phê duyệt.\n3. Gửi một trang in thử.");
        Guid DraftId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("knowledge-drafts-v1:" + key))[..16]);
        var expectedIds = new Dictionary<string, Guid> { ["password"] = DraftId("password-session"), ["attachments"] = DraftId("attachments"),
            ["notifications"] = DraftId("notifications"), ["vpn"] = vpn.Id, ["mfa"] = mfa.Id, ["printer"] = printer.Id };
        var cases = JsonSerializer.Deserialize<EvaluationCase[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Knowledge", "retrieval-evaluation.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var top1 = 0; var recall5 = 0; var positives = 0; var negativePass = 0;
        var failures = new List<string>();
        var observations = new List<object>();
        foreach (var item in cases)
        {
            var found = await SearchAsync(guest, item.Query);
            if (item.Expected is null)
            {
                observations.Add(new { item.Query, item.Expected, ResultCount = found.Items.Count });
                if (found.Items.Count == 0) negativePass++; else failures.Add($"Unexpected result: {item.Query}");
                continue;
            }
            positives++;
            var id = expectedIds[item.Expected];
            observations.Add(new { item.Query, item.Expected, Rank = found.Items.Select(x => x.EntryId).ToList().IndexOf(id) + 1,
                ExpectedStepFound = item.Step is null || found.Items.Any(x => x.EntryId == id && x.Step == item.Step) });
            if (found.Items.FirstOrDefault()?.EntryId == id) top1++;
            if (found.Items.Any(x => x.EntryId == id)) recall5++; else failures.Add($"Missing guide: {item.Query}");
            if (item.Step is { } step && !found.Items.Any(x => x.EntryId == id && x.Step == step)) failures.Add($"Missing step {step}: {item.Query}");
            foreach (var passage in found.Items)
            {
                Assert.InRange(passage.Text.Length, 1, 1000);
                Assert.Equal($"/knowledge/{passage.EntryId}", passage.SourceUrl);
                Assert.True(passage.Revision > 0);
                Assert.False(string.IsNullOrWhiteSpace(passage.SourceName));
            }
        }
        output.WriteLine($"Evaluation v1: Top1 {top1}/{positives}; Recall@5 {recall5}/{positives}; negative {negativePass}/{cases.Count(x => x.Expected is null)}");
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Knowledge", "retrieval-evaluation-result.json"),
            JsonSerializer.Serialize(new { Corpus = "isolated-test-only", At = DateTime.UtcNow, Top1 = top1, Recall5 = recall5, Positives = positives,
                NegativePass = negativePass, Negatives = cases.Count(x => x.Expected is null), Observations = observations }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(top1 >= positives * .85, $"Top1 {top1}/{positives} is below the 85% release gate.");
        Assert.Equal(positives, recall5);
    }

    [PostgresFact]
    public async Task OnlyOperationalSnapshotsAreRetrievedAndWithdrawalsAreImmediate()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var guest = test.Factory.CreateClient();
        var row = await AddGuideAsync(admin, "Cedarneedle vận hành", "Quy trình", "1. Dùng Cedarneedle public-approved-marker theo quy trình.");
        var demo = await AddGuideAsync(admin, "Cedarneedle demo", "Quy trình", "1. Cedarneedle demo-secret-marker.", "Demo");
        var draft = await CommunityTests.CreateAsync(admin, "knowledge");
        var ticket = await test.TicketAsync(test.AdminId, be.Domain.Enums.TicketStatus.New);
        await using (var privateDatabase = test.Database())
        {
            await privateDatabase.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Title, "Cedarneedle private Ticket").SetProperty(x => x.Description, "Cedarneedle private-ticket-marker"));
            privateDatabase.TicketComments.Add(new TicketComment { TicketId = ticket.Id, AuthorId = test.AdminId, IsInternal = true, Content = "Cedarneedle internal-note-marker" });
            // A legacy unclassified public snapshot must remain excluded even if someone bypassed publication validation.
            privateDatabase.CommunityEntries.Add(new CommunityEntry { Kind = CommunityKind.Knowledge, CreatedById = test.AdminId,
                Title = "Cedarneedle unclassified", Summary = "Cedarneedle", Content = "1. Cedarneedle unclassified-marker.", Topic = "Test", IsPublished = true,
                PublicTitle = "Cedarneedle unclassified", PublicSummary = "Cedarneedle", PublicContent = "1. Cedarneedle unclassified-marker.", PublicTopic = "Test",
                PublicRevision = 1, PublishedAt = DateTime.UtcNow });
            await privateDatabase.SaveChangesAsync();
        }
        using var edit = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{row.Id}", new SaveCommunityEntry("Cedarneedle draft-secret-marker", "Draft", "1. Cedarneedle draft-secret-marker.", row.Topic,
            Version: row.Version, Classification: "Demo", Guide: row.Guide));
        var edited = (await edit.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var result = await SearchAsync(guest, "Cedarneedle");
        Assert.Single(result.Items);
        Assert.Equal(row.Id, result.Items[0].EntryId);
        Assert.Contains("public-approved-marker", result.Items[0].Text);
        Assert.DoesNotContain("secret-marker", JsonSerializer.Serialize(result));
        Assert.Empty((await SearchAsync(guest, "draft-secret-marker")).Items);
        Assert.Empty((await SearchAsync(guest, "demo-secret-marker")).Items);
        using var body = await guest.GetAsync(Endpoint + "Cedarneedle");
        var json = await body.Content.ReadAsStringAsync();
        foreach (var privateField in new[] { "createdById", "sourceTicketId", "guideJson", "draft-secret-marker", "demo-secret-marker" }) Assert.DoesNotContain(privateField, json);
        Assert.True(body.Headers.CacheControl?.NoStore);
        await CommunityTests.PublishAsync(admin, "knowledge", edited); // reclassified snapshot leaves the operational index
        Assert.Empty((await SearchAsync(guest, "Cedarneedle")).Items);
        var republished = await AddGuideAsync(admin, "Cedarneedle mới", "Quy trình", "1. Cedarneedle replacement-public.");
        Assert.NotEmpty((await SearchAsync(guest, "Cedarneedle")).Items);
        using var withdrawn = await admin.PostAsJsonAsync($"/api/v1/community/knowledge/{republished.Id}/withdraw", new { republished.Version });
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Empty((await SearchAsync(guest, "Cedarneedle")).Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync(Endpoint + "VPN&limit=9")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync(Endpoint + "x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/v1/public/knowledge/retrieve")).StatusCode);
    }

    [PostgresFact]
    public async Task NormalizationMatchesPostgresAndMigrationRollbackPreservesContent()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        var row = await AddGuideAsync(admin, "ĐỔI MẬT KHẨU", "Tài khoản", "1. Chọn đổi mật khẩu ở trang Tài khoản.");
        await using var database = test.Database();
        foreach (var text in new[] { "ĐỔI MẬT KHẨU", "Đính kèm tệp!", "Tệp đính kèm", "MFA / 2FA _ Test-123", "tôi 😊" })
            Assert.Equal(KnowledgeRetrievalText.Normalize(text), await database.Database.SqlQuery<string>($"SELECT helpdesk.knowledge_search_normalize({text}) AS \"Value\"").SingleAsync());
        await database.GetService<IMigrator>().MigrateAsync("KnowledgeStandardization");
        Assert.True((await database.CommunityEntries.SingleAsync(x => x.Id == row.Id)).IsPublished);
        Assert.Equal(10, (await database.Database.GetAppliedMigrationsAsync()).Count());
        await database.GetService<IMigrator>().MigrateAsync();
        using var guest = test.Factory.CreateClient();
        Assert.NotEmpty((await SearchAsync(guest, "doi mat khau")).Items);
    }

    [PostgresFact]
    public async Task RealQueryUsesGinIndexWithSelectiveCorpusAndBoundsCandidatesAndPassages()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        await using var database = test.Database();
        var guide = new KnowledgeGuide("Máy trạm đã duyệt.", "Có tài khoản hợp lệ.", "Thành công.", "Liên hệ hỗ trợ.", "Evaluation fixture only");
        for (var i = 0; i < 1200; i++)
        {
            var word = i == 0 ? "cedarneedle" : "ordinaryword";
            database.CommunityEntries.Add(new CommunityEntry { Kind = CommunityKind.Knowledge, CreatedById = test.AdminId, Title = word, Summary = word,
                Content = $"1. {word} thực hiện theo hướng dẫn.", Topic = "Test", Guide = guide, Classification = CommunityClassification.Operational,
                IsPublished = true, PublicTitle = word, PublicSummary = word, PublicContent = $"1. {word} thực hiện theo hướng dẫn.", PublicTopic = "Test",
                PublicGuide = guide, PublicClassification = CommunityClassification.Operational, PublicRevision = 1, PublishedAt = DateTime.UtcNow });
        }
        await database.SaveChangesAsync();
        await database.Database.ExecuteSqlRawAsync("ANALYZE helpdesk.community_entries");
        await using var connection = new NpgsqlConnection(test.ConnectionString); await connection.OpenAsync();
        await using var plan = new NpgsqlCommand("""
            EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT)
            SELECT id FROM helpdesk.community_entries
            WHERE kind = 'Knowledge' AND is_published AND public_classification = 'Operational' AND public_guide IS NOT NULL
              AND helpdesk.knowledge_search_vector(public_title, public_summary, public_content, public_topic, public_guide) @@ to_tsquery('simple', 'cedarneedle')
            """, connection);
        await using var reader = await plan.ExecuteReaderAsync();
        var lines = new List<string>(); while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        var explain = string.Join('\n', lines); output.WriteLine(explain);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Knowledge", "retrieval-query-plan.txt"), explain);
        Assert.Contains("ix_community_knowledge_retrieval", explain);
        var store = new be.Infrastructure.Community.KnowledgeRetrievalStore(database);
        var candidates = await store.CandidatesAsync("ordinaryword", default);
        Assert.Equal(KnowledgeRetrievalService.CandidateLimit + 1, candidates.Count);
        var result = await new KnowledgeRetrievalService(store).SearchAsync(new("ordinaryword", 8), default);
        Assert.True(result.CandidateLimitReached); Assert.Equal(8, result.Items.Count);
        Assert.All(result.Items.GroupBy(x => x.EntryId), group => Assert.InRange(group.Count(), 1, 2));
    }

    private static async Task<ManagedCommunityEntry> AddGuideAsync(HttpClient admin, string title, string topic, string content, string classification = "Operational")
    {
        var guide = new KnowledgeGuide("Máy trạm và ứng dụng được đơn vị phê duyệt.", "Có tài khoản hợp lệ và kết nối Internet.", "Thao tác thành công theo hướng dẫn.",
            "Nếu chưa giải quyết được, liên hệ Helpdesk kèm mã lỗi.", "Evaluation fixture, not a production procedure", SourceUpdatedOn: new DateOnly(2026, 10, 30));
        using var response = await admin.PostAsJsonAsync("/api/v1/community/knowledge", new SaveCommunityEntry(title, title, content, topic, Classification: classification, Guide: guide));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await CommunityTests.PublishAsync(admin, "knowledge", (await response.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!);
    }
    private sealed record EvaluationCase(string Query, string? Expected, int? Step = null);
}
