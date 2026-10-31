using be.Application.Features.Accounts;
using be.Domain.Entities;

namespace be.Application.Features.Community;

public sealed record CommunitySearch(int Page = 1, int PageSize = 12, string? Search = null, string? Topic = null, bool? Active = null);
public sealed record SaveCommunityEntry(string Title, string Summary, string Content, string Topic, string? Status = null, uint Version = 0);
public sealed record PublishCommunityEntry(uint Version, bool ReviewedForPublic, string? Message = null);
public sealed record PublicCommunityEntry(Guid Id, string Kind, string Title, string Summary, string? Content, string Topic,
    string? Status, int Revision, DateTime PublishedAt, string SourceUrl, int AffectedCount, int HelpfulCount, int SolvedCount);
public sealed record ManagedCommunityEntry(Guid Id, string Kind, string Title, string Summary, string Content, string Topic,
    string? Status, bool IsPublished, int PublicRevision, DateTime? PublishedAt, uint Version, Guid? SourceTicketId, DateTime UpdatedAt);
public sealed record PublicIncidentUpdate(Guid Id, string Message, string Status, int Revision, DateTime CreatedAt);
public sealed record CommunityParticipation(bool Affected, bool? Helpful, bool Solved);

public interface ICommunityStore
{
    Task<KnowledgeImportResult> ImportAsync(AccountActor actor, ImportedKnowledgeDraft draft, CancellationToken ct);
    Task<PagedResult<PublicCommunityEntry>> PublicListAsync(CommunityKind kind, CommunitySearch search, CancellationToken ct);
    Task<PublicCommunityEntry> PublicGetAsync(CommunityKind kind, Guid id, CancellationToken ct);
    Task<PagedResult<PublicIncidentUpdate>> UpdatesAsync(Guid id, CommunitySearch search, CancellationToken ct);
    Task<PagedResult<ManagedCommunityEntry>> ManageListAsync(AccountActor actor, CommunityKind kind, CommunitySearch search, CancellationToken ct);
    Task<ManagedCommunityEntry> ManageGetAsync(AccountActor actor, CommunityKind kind, Guid id, CancellationToken ct);
    Task<ManagedCommunityEntry> SaveAsync(AccountActor actor, CommunityKind kind, Guid? id, SaveCommunityEntry command, CancellationToken ct);
    Task<ManagedCommunityEntry> FromTicketAsync(AccountActor actor, Guid ticketId, CancellationToken ct);
    Task<ManagedCommunityEntry> PublishAsync(AccountActor actor, CommunityKind kind, Guid id, PublishCommunityEntry command, CancellationToken ct);
    Task<ManagedCommunityEntry> WithdrawAsync(AccountActor actor, CommunityKind kind, Guid id, uint version, CancellationToken ct);
    Task<CommunityParticipation> ParticipationAsync(AccountActor actor, CommunityKind kind, Guid id, CancellationToken ct);
    Task<CommunityParticipation> ParticipateAsync(AccountActor actor, CommunityKind kind, Guid id, bool helpfulOrAffected, bool solved, CancellationToken ct);
}
public sealed class CommunityService(ICommunityStore store)
{
    public static CommunityKind Kind(string kind) => kind switch { "knowledge" => CommunityKind.Knowledge, "incidents" => CommunityKind.Incident,
        _ => throw new CommunityOperationException(CommunityError.InvalidInput) };
    public Task<PagedResult<PublicCommunityEntry>> PublicListAsync(CommunityKind kind, CommunitySearch search, CancellationToken ct) => store.PublicListAsync(kind, Search(search), ct);
    public Task<PublicCommunityEntry> PublicGetAsync(CommunityKind kind, Guid id, CancellationToken ct) => store.PublicGetAsync(kind, id, ct);
    public Task<PagedResult<PublicIncidentUpdate>> UpdatesAsync(Guid id, CommunitySearch search, CancellationToken ct) => store.UpdatesAsync(id, Search(search), ct);
    public Task<PagedResult<ManagedCommunityEntry>> ManageListAsync(AccountActor actor, CommunityKind kind, CommunitySearch search, CancellationToken ct) => store.ManageListAsync(actor, kind, Search(search), ct);
    public Task<ManagedCommunityEntry> ManageGetAsync(AccountActor actor, CommunityKind kind, Guid id, CancellationToken ct) => store.ManageGetAsync(actor, kind, id, ct);
    public Task<ManagedCommunityEntry> SaveAsync(AccountActor actor, CommunityKind kind, Guid? id, SaveCommunityEntry command, CancellationToken ct)
    {
        if ((id is not null && command.Version == 0) || (kind == CommunityKind.Knowledge && command.Status is not null) ||
            (kind == CommunityKind.Incident && (!Enum.TryParse<IncidentStatus>(command.Status, out var status) || !Enum.IsDefined(status)))) Invalid();
        return store.SaveAsync(actor, kind, id, command with { Title = Text(command.Title, 200), Summary = Text(command.Summary, 1000),
            Content = Text(command.Content, 30000), Topic = Text(command.Topic, 100) }, ct);
    }
    public Task<ManagedCommunityEntry> FromTicketAsync(AccountActor actor, Guid ticketId, CancellationToken ct) => store.FromTicketAsync(actor, ticketId, ct);
    public Task<ManagedCommunityEntry> PublishAsync(AccountActor actor, CommunityKind kind, Guid id, PublishCommunityEntry command, CancellationToken ct)
    {
        if (command.Version == 0 || !command.ReviewedForPublic || command.Message?.Length > 1000) Invalid();
        return store.PublishAsync(actor, kind, id, command with { Message = command.Message?.Trim() }, ct);
    }
    public Task<ManagedCommunityEntry> WithdrawAsync(AccountActor actor, CommunityKind kind, Guid id, uint version, CancellationToken ct)
    {
        if (version == 0) Invalid();
        return store.WithdrawAsync(actor, kind, id, version, ct);
    }
    public Task<CommunityParticipation> ParticipationAsync(AccountActor actor, CommunityKind kind, Guid id, CancellationToken ct) => store.ParticipationAsync(actor, kind, id, ct);
    public Task<CommunityParticipation> ParticipateAsync(AccountActor actor, CommunityKind kind, Guid id, bool value, bool solved, CancellationToken ct)
    {
        if (solved && (!value || kind == CommunityKind.Incident)) Invalid();
        return store.ParticipateAsync(actor, kind, id, value, solved, ct);
    }
    private static CommunitySearch Search(CommunitySearch search)
    {
        if (search.Page is < 1 or > 100000 || search.PageSize is < 1 or > 50 || search.Search?.Length > 200 || search.Topic?.Length > 100) Invalid();
        return search with { Search = search.Search?.Trim(), Topic = search.Topic?.Trim() };
    }
    private static string Text(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit ? value.Trim()
        : throw new CommunityOperationException(CommunityError.InvalidInput);
    private static void Invalid() => throw new CommunityOperationException(CommunityError.InvalidInput);
}
