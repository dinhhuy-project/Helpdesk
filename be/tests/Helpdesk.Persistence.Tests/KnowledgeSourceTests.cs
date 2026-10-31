using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using be.Application.Features.Community;
using be.Configuration;
using be.Domain.Constants;
using be.Infrastructure.Community;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class KnowledgeSourceTests
{
    internal static KnowledgeSourcesOptions Settings() => new()
    {
        BookStack = new() { Enabled = true, BaseUrl = "https://private-wiki.example.test/", SourceId = "test-support", BookId = 7,
            TokenId = "TestTokenIdOnly", TokenSecret = "TestTokenSecretOnly", Attribution = "Helpdesk authors", License = "Organization approved publication" }
    };
    internal static string Metadata(int book = 7, int revision = 2, bool draft = false) => JsonSerializer.Serialize(new
        { id = 12, book_id = book, name = "VPN setup", updated_at = "2026-10-31T00:00:00.000000Z", revision_count = revision, draft, template = false });
    internal static HttpResponseMessage Reply(string data, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(data, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task WikipediaUsesFixedHostNoCredentialsAndCachesOnlyPublicLinks()
    {
        using var http = new SourceHttpFactory(_ => Reply("""{"query":{"search":[{"pageid":123,"title":"Mạng riêng ảo","timestamp":"2026-10-30T12:30:00Z"}]},"continue":{"sroffset":5}}"""));
        using var sources = new KnowledgeSources(http, Options.Create(Settings()));
        var result = await sources.WikipediaAsync("VPN + bảo mật", 0, default);
        Assert.Equal("https://vi.wikipedia.org/?curid=123", Assert.Single(result.Items).Url);
        Assert.Equal(5, result.NextOffset);
        Assert.Equal(result, await sources.WikipediaAsync("VPN + bảo mật", 0, default));
        var request = Assert.Single(http.Requests);
        Assert.Equal("vi.wikipedia.org", request.Uri.Host); Assert.Null(request.Authorization); Assert.Null(request.Cookie);
        Assert.Contains("srprop=timestamp", request.Uri.Query);
    }
    [Theory]
    [InlineData("not-json", 200)]
    [InlineData("{}", 200)]
    [InlineData("{\"error\":{\"code\":\"maxlag\"}}", 200)]
    [InlineData("{}", 301)]
    [InlineData("{}", 429)]
    [InlineData("{}", 500)]
    public async Task RemoteErrorsBecomeSafeServiceUnavailable(string body, int status)
    {
        using var http = new SourceHttpFactory(_ => Reply(body, (HttpStatusCode)status));
        using var sources = new KnowledgeSources(http, Options.Create(Settings()));
        var error = await Assert.ThrowsAsync<CommunityOperationException>(() => sources.WikipediaAsync("VPN", 0, default));
        Assert.Equal(CommunityError.SourceUnavailable, error.Error);
    }
    [Fact]
    public async Task DisabledSourcesDoNotSendRequests()
    {
        var settings = Settings(); settings.WikipediaEnabled = false; settings.BookStack.Enabled = false;
        using var http = new SourceHttpFactory(_ => throw new InvalidOperationException("Must not access network."));
        using var sources = new KnowledgeSources(http, Options.Create(settings));
        Assert.Equal(CommunityError.SourceDisabled, (await Assert.ThrowsAsync<CommunityOperationException>(() => sources.WikipediaAsync("VPN", 0, default))).Error);
        Assert.Equal(CommunityError.SourceDisabled, (await Assert.ThrowsAsync<CommunityOperationException>(() => sources.BookStackPagesAsync("VPN", 1, default))).Error);
        Assert.Empty(http.Requests);
    }
    [Theory]
    [InlineData(8, false)]
    [InlineData(7, true)]
    public async Task PagesOutsideApprovedBookOrStillDraftCannotBeExported(int book, bool draft)
    {
        using var http = new SourceHttpFactory(_ => Reply(Metadata(book, draft: draft)));
        using var sources = new KnowledgeSources(http, Options.Create(Settings()));
        Assert.Equal(CommunityError.NotFound, (await Assert.ThrowsAsync<CommunityOperationException>(() => sources.BookStackPreviewAsync(12, default))).Error);
        Assert.Single(http.Requests);
    }
    [Fact]
    public async Task PlaintextImportKeepsSourceRevisionAndAttributionWithoutPrivateAddressOrToken()
    {
        using var http = new SourceHttpFactory(r => Reply(r.RequestUri!.AbsolutePath.EndsWith("plaintext") ? "Check VPN configuration." : Metadata()));
        using var sources = new KnowledgeSources(http, Options.Create(Settings()));
        var preview = await sources.BookStackPreviewAsync(12, default);
        var draft = sources.Draft(preview);
        Assert.Null(preview.SourceUrl);
        Assert.Contains("Check VPN configuration.", draft.Content); Assert.Contains("Helpdesk authors", draft.Content);
        Assert.DoesNotContain("private-wiki", draft.Content); Assert.DoesNotContain("TestToken", draft.Content);
        Assert.Equal(draft.Id, sources.Draft(preview with { Content = "Source was edited." }).Id);
        Assert.All(http.Requests, r => Assert.Equal("Token TestTokenIdOnly:TestTokenSecretOnly", r.Authorization));
        Assert.Equal(3, http.Requests.Count);
    }
    [Fact]
    public async Task ChangingWikiRevisionDuringExportRequiresReviewAgain()
    {
        var reads = 0;
        using var http = new SourceHttpFactory(r => Reply(r.RequestUri!.AbsolutePath.EndsWith("plaintext") ? "Body" : Metadata(revision: ++reads)));
        using var sources = new KnowledgeSources(http, Options.Create(Settings()));
        Assert.Equal(CommunityError.VersionConflict, (await Assert.ThrowsAsync<CommunityOperationException>(() => sources.BookStackPreviewAsync(12, default))).Error);
    }
    [Fact]
    public async Task ExcessiveResponseIsRejectedBeforeParsing()
    {
        using var http = new SourceHttpFactory(_ => Reply(new string('x', 1048577)));
        using var sources = new KnowledgeSources(http, Options.Create(Settings()));
        Assert.Equal(CommunityError.SourceUnavailable, (await Assert.ThrowsAsync<CommunityOperationException>(() => sources.WikipediaAsync("VPN", 0, default))).Error);
    }
    [Fact]
    public void EnabledSourceRequiresExplicitSafeConfiguration()
    {
        Assert.True(KnowledgeSourcesRegistration.Valid(Settings()));
        var o = Settings(); o.BookStack.BaseUrl = "http://wiki/"; Assert.False(KnowledgeSourcesRegistration.Valid(o));
        o.BookStack.AllowHttp = true; Assert.True(KnowledgeSourcesRegistration.Valid(o));
        o.BookStack.BaseUrl = "https://user:password@wiki/"; Assert.False(KnowledgeSourcesRegistration.Valid(o));
        o = Settings(); o.BookStack.TokenSecret = "newline\r\nheader"; Assert.False(KnowledgeSourcesRegistration.Valid(o));
        o = Settings(); o.BookStack.BookId = 0; Assert.False(KnowledgeSourcesRegistration.Valid(o));
        o = Settings(); o.UserAgent = "HelpdeskCommunity/1.0 (support@example.test)"; Assert.True(KnowledgeSourcesRegistration.Valid(o));
    }
}

internal sealed class SourceHttpFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
{
    public List<(Uri Uri, string? Authorization, string? Cookie)> Requests { get; } = [];
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join(";", cookies) : null));
        return Task.FromResult(respond(request));
    }
}

[Collection("Postgres")]
public class KnowledgeImportTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task ImportedArticleIsCreateOnceAndNeverOverwritesEditsOrPublishedSnapshot()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var http = new SourceHttpFactory(r => KnowledgeSourceTests.Reply(r.RequestUri!.AbsolutePath.EndsWith("plaintext") ? "Wiki steps that require review." : KnowledgeSourceTests.Metadata()));
        await using var factory = test.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton<IHttpClientFactory>(http);
            services.Configure<KnowledgeSourcesOptions>(o => o.BookStack = KnowledgeSourceTests.Settings().BookStack);
        }));
        using var session = await test.AdminAsync(); using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = session.DefaultRequestHeaders.Authorization;
        using var guest = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/v1/community/knowledge-sources/bookstack/imports", new { pageId = 12 })).StatusCode);
        Assert.Empty(http.Requests);
        using var response = await admin.PostAsJsonAsync("/api/v1/community/knowledge-sources/bookstack/imports", new { pageId = 12 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var imported = (await response.Content.ReadFromJsonAsync<KnowledgeImportResult>())!;
        Assert.False(imported.AlreadyImported); Assert.False(imported.Entry.IsPublished);
        Assert.Contains("Nguồn biên soạn: BookStack", imported.Entry.Content);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/knowledge/{imported.Entry.Id}")).StatusCode);
        var published = await CommunityTests.PublishAsync(admin, "knowledge", imported.Entry);
        using var save = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{published.Id}", new SaveCommunityEntry("Editor owns this title", "Summary", "Private new draft", "VPN", Version: published.Version));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var repeat = await admin.PostAsJsonAsync("/api/v1/community/knowledge-sources/bookstack/imports", new { pageId = 12 });
        var existing = (await repeat.Content.ReadFromJsonAsync<KnowledgeImportResult>())!;
        Assert.True(existing.AlreadyImported); Assert.Equal(imported.Entry.Id, existing.Entry.Id); Assert.Equal("Editor owns this title", existing.Entry.Title);
        var visible = await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/knowledge/{existing.Entry.Id}");
        Assert.Equal("VPN setup", visible!.Title); Assert.DoesNotContain("Private new draft", visible.Content!);
        await using var database = test.Database();
        Assert.Single(await database.CommunityEntries.ToListAsync());
        Assert.Single(await database.IdentityAuditLogs.Where(x => x.Action == "KnowledgeImported").ToListAsync());
    }
    [PostgresFact]
    public async Task OrdinaryParticipantCannotReadPrivateWikiAndPublicSearchRejectsInvalidInput()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var user = await test.UserAsync(PermissionCodes.KnowledgePublish);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/community/knowledge-sources/bookstack/pages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/v1/community/knowledge-sources/bookstack/imports", new { pageId = 12 })).StatusCode);
        using var guest = test.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/api/v1/public/knowledge-sources")).StatusCode);
        foreach (var query in new[] { "search=", "search=VPN&offset=-1", "search=VPN&offset=501", "search=" + new string('a', 151) })
            Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/v1/public/knowledge-sources/wikipedia?" + query)).StatusCode);
    }
}
