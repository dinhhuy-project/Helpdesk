using be.Application.Common.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Domain.Constants;

namespace be.Application.Features.Community;

public sealed record KnowledgeSourceStatus(bool WikipediaEnabled, bool BookStackEnabled);
public sealed record WikiReference(string Title, string Url, DateTime? UpdatedAt);
public sealed record WikipediaSearchResult(IReadOnlyList<WikiReference> Items, int? NextOffset);
public sealed record BookStackPage(int Id, string Title, string? UpdatedAt);
public sealed record BookStackPreview(int PageId, string Title, string Content, string Revision, string? SourceUrl,
    string Attribution, string License);
public sealed record ImportedKnowledgeDraft(Guid Id, string Title, string Content, string SourceId, int PageId, string Revision);
public sealed record KnowledgeImportResult(ManagedCommunityEntry Entry, bool AlreadyImported);

public interface IKnowledgeSources
{
    KnowledgeSourceStatus Status { get; }
    Task<WikipediaSearchResult> WikipediaAsync(string search, int offset, CancellationToken ct);
    Task<PagedResult<BookStackPage>> BookStackPagesAsync(string search, int page, CancellationToken ct);
    Task<BookStackPreview> BookStackPreviewAsync(int pageId, CancellationToken ct);
    ImportedKnowledgeDraft Draft(BookStackPreview preview);
}

public sealed class KnowledgeSourceService(IKnowledgeSources sources, ICommunityStore store, IAuthSessionStore sessions, IPermissionChecker permissions)
{
    public KnowledgeSourceStatus Status => sources.Status;
    public Task<WikipediaSearchResult> WikipediaAsync(string? search, int offset, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Length > 150 || offset is < 0 or > 500 || search.Any(char.IsControl))
            throw new CommunityOperationException(CommunityError.InvalidInput);
        return sources.WikipediaAsync(search.Trim(), offset, ct);
    }
    public async Task<PagedResult<BookStackPage>> PagesAsync(AccountActor actor, string? search, int page, CancellationToken ct)
    {
        await RequireAsync(actor, ct);
        if (page is < 1 or > 1000 || search?.Length > 150 || search?.Any(char.IsControl) == true) throw new CommunityOperationException(CommunityError.InvalidInput);
        var result = await sources.BookStackPagesAsync(search?.Trim() ?? "", page, ct);
        await RequireAsync(actor, ct);
        return result;
    }
    public async Task<BookStackPreview> PreviewAsync(AccountActor actor, int pageId, CancellationToken ct)
    {
        await RequireAsync(actor, ct);
        if (pageId <= 0) throw new CommunityOperationException(CommunityError.InvalidInput);
        var result = await sources.BookStackPreviewAsync(pageId, ct);
        await RequireAsync(actor, ct);
        return result;
    }
    public async Task<KnowledgeImportResult> ImportAsync(AccountActor actor, int pageId, CancellationToken ct)
    {
        // Fetch before opening the database transaction. ImportAsync rechecks authorization under its write lock.
        var preview = await PreviewAsync(actor, pageId, ct);
        return await store.ImportAsync(actor, sources.Draft(preview), ct);
    }
    private async Task RequireAsync(AccountActor actor, CancellationToken ct)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, ct)) throw new CommunityOperationException(CommunityError.Unauthenticated);
        if (!await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, PermissionCodes.KnowledgeManage, ct))
            throw new CommunityOperationException(CommunityError.InsufficientScope);
    }
}
