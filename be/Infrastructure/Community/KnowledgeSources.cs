using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Community;
using be.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace be.Infrastructure.Community;

public sealed class KnowledgeSources(IHttpClientFactory clients, IOptions<KnowledgeSourcesOptions> options) : IKnowledgeSources, IDisposable
{
    public const string ClientName = "KnowledgeSources";
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 128 });
    private KnowledgeSourcesOptions Settings => options.Value;
    public KnowledgeSourceStatus Status => new(Settings.WikipediaEnabled, Settings.BookStack.Enabled);
    private static CommunityOperationException Unavailable() => new(CommunityError.SourceUnavailable);

    public async Task<WikipediaSearchResult> WikipediaAsync(string search, int offset, CancellationToken ct)
    {
        if (!Settings.WikipediaEnabled) throw new CommunityOperationException(CommunityError.SourceDisabled);
        var key = (search, offset);
        if (cache.TryGetValue(key, out WikipediaSearchResult? hit) && hit is not null) return hit;
        // Titles/links only: no encyclopedia article is copied into the organization's approved corpus.
        var url = "https://vi.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&list=search&srnamespace=0&srlimit=5&srprop=timestamp&maxlag=5&srsearch="
            + Uri.EscapeDataString(search) + "&sroffset=" + offset.ToString(CultureInfo.InvariantCulture);
        using var data = await JsonAsync(url, false, ct);
        try
        {
            if (data.RootElement.TryGetProperty("error", out _)) throw Unavailable();
            var items = data.RootElement.GetProperty("query").GetProperty("search").EnumerateArray().Select(x =>
            {
                var id = x.GetProperty("pageid").GetInt32();
                if (id <= 0) throw Unavailable();
                var title = x.GetProperty("title").GetString() ?? throw Unavailable();
                if (title.Length > 500) throw Unavailable();
                DateTime? updated = DateTime.TryParse(x.GetProperty("timestamp").GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
                return new WikiReference(title, "https://vi.wikipedia.org/?curid=" + id.ToString(CultureInfo.InvariantCulture), updated);
            }).Take(5).ToArray();
            int? next = data.RootElement.TryGetProperty("continue", out var continuation) && continuation.TryGetProperty("sroffset", out var nextOffset)
                ? nextOffset.GetInt32() : null;
            if (next is <= 0 or > 500) next = null;
            var result = new WikipediaSearchResult(items, next);
            cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5), Size = 1 });
            return result;
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { throw Unavailable(); }
    }

    public async Task<PagedResult<BookStackPage>> BookStackPagesAsync(string search, int page, CancellationToken ct)
    {
        RequireBookStack();
        var query = $"api/pages?count=12&offset={(page - 1) * 12}&sort=-updated_at&filter[book_id]={Settings.BookStack.BookId}&filter[draft]=false&filter[template]=false";
        if (search.Length > 0) query += "&filter[name:like]=" + Uri.EscapeDataString("%" + search + "%");
        using var data = await JsonAsync(BookStackUrl(query), true, ct);
        try
        {
            var items = data.RootElement.GetProperty("data").EnumerateArray().Select(x =>
            {
                if (x.GetProperty("book_id").GetInt32() != Settings.BookStack.BookId) throw Unavailable();
                return new BookStackPage(x.GetProperty("id").GetInt32(), x.GetProperty("name").GetString() ?? "", x.GetProperty("updated_at").GetString());
            }).Take(12).ToArray();
            return new(items, page, 12, data.RootElement.GetProperty("total").GetInt32());
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { throw Unavailable(); }
    }

    public async Task<BookStackPreview> BookStackPreviewAsync(int pageId, CancellationToken ct)
    {
        RequireBookStack();
        var metadata = await MetadataAsync(pageId, ct);
        var content = (await ReadAsync(BookStackUrl($"api/pages/{pageId}/export/plaintext"), true, ct)).Trim();
        var after = await MetadataAsync(pageId, ct);
        if (metadata != after) throw new CommunityOperationException(CommunityError.VersionConflict);
        if (string.IsNullOrWhiteSpace(content) || content.Contains('\0') || content.Length > 28000) throw new CommunityOperationException(CommunityError.InvalidInput);
        var publicUrl = string.IsNullOrWhiteSpace(Settings.BookStack.PublicBaseUrl) ? null : Settings.BookStack.PublicBaseUrl.TrimEnd('/') + "/link/" + pageId;
        return new(pageId, Cut(metadata.Title, 200), content, metadata.Revision, publicUrl, Settings.BookStack.Attribution, Settings.BookStack.License);
    }
    private async Task<(string Title, string Revision)> MetadataAsync(int id, CancellationToken ct)
    {
        using var data = await JsonAsync(BookStackUrl($"api/pages/{id}"), true, ct);
        try
        {
            var root = data.RootElement;
            if (root.GetProperty("id").GetInt32() != id || root.GetProperty("book_id").GetInt32() != Settings.BookStack.BookId ||
                root.GetProperty("draft").GetBoolean() || root.GetProperty("template").GetBoolean())
                throw new CommunityOperationException(CommunityError.NotFound);
            return (root.GetProperty("name").GetString() ?? throw Unavailable(),
                root.GetProperty("revision_count").GetInt32() + " · " + root.GetProperty("updated_at").GetString());
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException) { throw Unavailable(); }
    }
    public ImportedKnowledgeDraft Draft(BookStackPreview preview)
    {
        var identity = "helpdesk-bookstack-v1:" + Settings.BookStack.SourceId + ":" + preview.PageId.ToString(CultureInfo.InvariantCulture);
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
        var footer = "\n\n---\nNguồn biên soạn: BookStack · Trang " + preview.PageId + "\nPhiên bản nguồn: " + preview.Revision
            + "\nGhi công: " + preview.Attribution + "\nQuyền sử dụng: " + preview.License
            + (preview.SourceUrl is null ? "" : "\nBài gốc: " + preview.SourceUrl);
        if (string.IsNullOrWhiteSpace(preview.Title) || preview.Title.Contains('\0') || preview.Content.Length + footer.Length > 30000)
            throw new CommunityOperationException(CommunityError.InvalidInput);
        return new(id, preview.Title, preview.Content + footer, Settings.BookStack.SourceId, preview.PageId, preview.Revision);
    }
    private void RequireBookStack()
    {
        if (!Settings.BookStack.Enabled) throw new CommunityOperationException(CommunityError.SourceDisabled);
    }
    private string BookStackUrl(string path) => Settings.BookStack.BaseUrl.TrimEnd('/') + "/" + path;
    private static string Cut(string text, int length) => text.Length <= length ? text.Trim() : text[..(char.IsHighSurrogate(text[length - 1]) ? length - 1 : length)].Trim();
    private async Task<JsonDocument> JsonAsync(string url, bool authenticated, CancellationToken ct)
    {
        try { return JsonDocument.Parse(await ReadAsync(url, authenticated, ct)); }
        catch (JsonException) { throw Unavailable(); }
    }
    private async Task<string> ReadAsync(string url, bool authenticated, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(Settings.UserAgent);
            if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Token", Settings.BookStack.TokenId + ":" + Settings.BookStack.TokenSecret);
            using var client = clients.CreateClient(ClientName);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.NotFound && authenticated) throw new CommunityOperationException(CommunityError.NotFound);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1048576) throw Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(bytes, deadline.Token)) != 0)
            {
                if (buffer.Length + count > 1048576) throw Unavailable();
                await buffer.WriteAsync(bytes.AsMemory(0, count), deadline.Token);
            }
            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
        catch (Exception error) when (error is HttpRequestException or IOException) { throw Unavailable(); }
    }
    public void Dispose() => cache.Dispose();
}
