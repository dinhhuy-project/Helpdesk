using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Community;
using be.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class KnowledgeAssistantBoundaryTests
{
    [Fact]
    public async Task AnonymousPostReturnsNoStoreAndExplicitDisabledStateWithoutDatabaseOrCredentials()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/public/assistant", new { question = "VPN", model = "cloud-secret-marker", context = "private-secret-marker" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<KnowledgeAssistantResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("Unavailable", result.Status); Assert.Empty(result.Sources); Assert.Contains("Helpdesk", result.Contact.Message);
        Assert.DoesNotContain("secret-marker", body);
        using var invalid = await client.PostAsJsonAsync("/api/v1/public/assistant", new { question = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/public/assistant", new { })).StatusCode);
    }
    [Fact]
    public async Task AnonymousInferenceHasItsOwnSixPerMinuteLimitWithRetryAfter()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        for (var i = 0; i < 6; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/public/assistant", new { question = "VPN" })).StatusCode);
        using var rejected = await client.PostAsJsonAsync("/api/v1/public/assistant", new { question = "VPN" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode); Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
    }
}

[Collection("Postgres")]
public class KnowledgeAssistantApiTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task OnlyOperationalPublicSnapshotsReachTheModelAndSourcesAreServerGenerated()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        var published = await CommunityTests.PublishAsync(admin, "knowledge", await CommunityTests.CreateAsync(admin, "knowledge"));
        var draft = await CommunityTests.CreateAsync(admin, "knowledge");
        using var saveDemo = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{draft.Id}", new SaveCommunityEntry("VPN demo-secret-marker", "VPN", "1. VPN demo-secret-marker.", "Network",
            Version: draft.Version, Classification: "Demo", Guide: draft.Guide));
        await CommunityTests.PublishAsync(admin, "knowledge", (await saveDemo.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!);
        _ = await CommunityTests.CreateAsync(admin, "knowledge");
        var ticket = await test.TicketAsync(test.AdminId, be.Domain.Enums.TicketStatus.New);
        await using (var database = test.Database())
        {
            await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.Description, "VPN private-ticket-marker"));
            database.TicketComments.Add(new TicketComment { TicketId = ticket.Id, AuthorId = test.AdminId, IsInternal = true, Content = "VPN internal-note-marker" });
            await database.SaveChangesAsync();
        }
        var selector = new AssistantTestModel { OnSelect = (_, passages, _) =>
        {
            Assert.All(passages, p => Assert.Equal(published.Id, p.EntryId));
            var json = JsonSerializer.Serialize(passages); Assert.DoesNotContain("secret-marker", json); Assert.DoesNotContain("private-ticket-marker", json); Assert.DoesNotContain("internal-note-marker", json);
            return Task.FromResult(new KnowledgeSelection(true, [1]));
        } };
        await using var api = test.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["LocalAssistant:Enabled"] = "true" }));
            builder.ConfigureTestServices(services => { services.RemoveAll<ILocalKnowledgeModel>(); services.AddSingleton<ILocalKnowledgeModel>(selector); });
        });
        using var client = api.CreateClient();
        var answer = await AskAsync(client, "VPN"); Assert.Equal("Answered", answer.Status); Assert.Single(answer.Sources);
        Assert.Equal(published.Id, answer.Sources[0].Passage.EntryId); Assert.Equal(published.PublicRevision, answer.Sources[0].Passage.Revision);
        Assert.Equal(1, selector.Calls);
        var absent = await AskAsync(client, "quantumteleportation");
        Assert.Equal("InsufficientInformation", absent.Status); Assert.Empty(absent.Sources); Assert.Equal(1, selector.Calls);
    }

    [PostgresTheory]
    [InlineData("withdraw")]
    [InlineData("republish")]
    [InlineData("reclassify")]
    public async Task PublicationChangesDuringInferenceInvalidateTheEntireAnswer(string change)
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString); using var admin = await test.AdminAsync();
        var row = await CommunityTests.PublishAsync(admin, "knowledge", await CommunityTests.CreateAsync(admin, "knowledge"));
        var selector = new AssistantTestModel { OnSelect = async (_, _, _) =>
        {
            if (change == "withdraw")
                Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/community/knowledge/{row.Id}/withdraw", new { row.Version })).StatusCode);
            else
            {
                using var response = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{row.Id}", new SaveCommunityEntry(row.Title, row.Summary, "1. VPN updated procedure.", row.Topic,
                    Version: row.Version, Classification: change == "reclassify" ? "Demo" : "Operational", Guide: row.Guide));
                await CommunityTests.PublishAsync(admin, "knowledge", (await response.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!);
            }
            return new KnowledgeSelection(true, [1]);
        } };
        await using var api = test.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["LocalAssistant:Enabled"] = "true" }));
            builder.ConfigureTestServices(services => { services.RemoveAll<ILocalKnowledgeModel>(); services.AddSingleton<ILocalKnowledgeModel>(selector); });
        });
        using var client = api.CreateClient(); var answer = await AskAsync(client, "VPN");
        Assert.Equal("SourcesChanged", answer.Status); Assert.Empty(answer.Sources); Assert.Equal(1, selector.Calls);
        Assert.DoesNotContain("Check VPN settings", answer.Answer);
    }

    [LocalAssistantPostgresFact]
    public async Task ActualLocalModelAnswersWithReviewedSourcesAndAbstainsWithoutEvidence()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString); using var admin = await test.AdminAsync();
        var guide = new KnowledgeGuide("Người dùng đang đăng nhập vào Helpdesk, biết mật khẩu hiện tại.", "Mở được trang Tài khoản của tôi.", "Đăng nhập lại bằng mật khẩu mới.",
            "Nếu chưa giải quyết được, liên hệ Helpdesk qua Ticket.", "Isolated local-model evaluation fixture");
        using var create = await admin.PostAsJsonAsync("/api/v1/community/knowledge", new SaveCommunityEntry("Đổi mật khẩu và đăng nhập lại", "Tự thay đổi mật khẩu hiện tại.",
            "1. Mở trang Tài khoản của tôi.\n2. Chọn đổi mật khẩu, nhập mật khẩu hiện tại và mật khẩu mới.\n3. Sau khi đổi thành công, đăng nhập lại bằng mật khẩu mới.", "Tài khoản", Classification: "Operational", Guide: guide));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var row = await CommunityTests.PublishAsync(admin, "knowledge", (await create.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!);
        await using var api = test.Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LocalAssistant:Enabled"] = "true", ["LocalAssistant:BaseUrl"] = Environment.GetEnvironmentVariable("HELPDESK_TEST_OLLAMA_URL"),
            ["LocalAssistant:Model"] = Environment.GetEnvironmentVariable("HELPDESK_TEST_OLLAMA_MODEL") ?? "qwen3:1.7b", ["LocalAssistant:TimeoutSeconds"] = "120"
        })));
        using var client = api.CreateClient(); client.Timeout = TimeSpan.FromSeconds(130);
        var timer = Stopwatch.StartNew(); var answer = await AskAsync(client, "doi mat khau"); timer.Stop();
        Assert.Equal("Answered", answer.Status); Assert.Contains(answer.Sources, source => source.Passage.Step == 2);
        Assert.All(answer.Sources, source => { Assert.Equal(row.Id, source.Passage.EntryId); Assert.Contains(source.Passage.Text, answer.Answer); });
        var absent = await AskAsync(client, "thời tiết ngày mai"); Assert.Equal("InsufficientInformation", absent.Status); Assert.Empty(absent.Sources);
        using var scope = api.Services.CreateScope(); var model = scope.ServiceProvider.GetRequiredService<ILocalKnowledgeModel>();
        var unrelated = await model.SelectAsync("Giá cổ phiếu ngày mai là bao nhiêu?", [OllamaKnowledgeModelTests.Passage()], default);
        Assert.False(unrelated.Sufficient); Assert.Empty(unrelated.PassageIds);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Knowledge", "local-assistant-real-model-result.json"),
            JsonSerializer.Serialize(new { At = DateTime.UtcNow, Model = Environment.GetEnvironmentVariable("HELPDESK_TEST_OLLAMA_MODEL") ?? "qwen3:1.7b",
                answer.Status, Sources = answer.Sources.Count, DurationMs = timer.ElapsedMilliseconds, NegativeStatus = absent.Status, ModelAbstained = !unrelated.Sufficient }));
    }
    private static async Task<KnowledgeAssistantResponse> AskAsync(HttpClient client, string question)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/public/assistant", new { question });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<KnowledgeAssistantResponse>())!;
    }
}

public sealed class LocalAssistantPostgresFactAttribute : FactAttribute
{
    public LocalAssistantPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION")) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HELPDESK_TEST_OLLAMA_URL")))
            Skip = "Requires an isolated PostgreSQL test database and an explicitly enabled local Ollama model.";
    }
}
