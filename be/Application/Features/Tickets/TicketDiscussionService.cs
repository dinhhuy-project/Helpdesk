using be.Application.Features.Accounts;

namespace be.Application.Features.Tickets;

public sealed record CreateTicketComment(string Content, bool IsInternal = false);
public sealed record TicketDiscussionSearch(int Page = 1, int PageSize = 20, bool Descending = false, bool? IsInternal = null);
public sealed record TicketCommentDetails(Guid Id, Guid TicketId, Guid AuthorId, string Content, bool IsInternal,
    DateTime CreatedAt, DateTime? UpdatedAt);
public sealed record TicketActivityData(long? TicketNumber = null, Guid? RequesterId = null, Guid? CreatedByUserId = null,
    Guid? CategoryId = null, string? Status = null, string? Priority = null, Guid? AssignedTeamId = null, Guid? AssignedAgentId = null,
    string? ResolutionSummary = null, DateTime? ResolvedAt = null, DateTime? ClosedAt = null, Guid? CommentId = null, bool? IsInternal = null, Guid? AttachmentId = null);
public sealed record TicketActivityDetails(Guid Id, Guid TicketId, Guid? ActorId, string Action, DateTime CreatedAt,
    TicketActivityData? OldValue, TicketActivityData? NewValue);

public interface ITicketDiscussionStore
{
    Task<TicketCommentDetails> CreateAsync(AccountActor actor, Guid ticketId, CreateTicketComment command, CancellationToken cancellationToken);
    Task<PagedResult<TicketCommentDetails>> ListCommentsAsync(AccountActor actor, Guid ticketId, TicketDiscussionSearch search, CancellationToken cancellationToken);
    Task<TicketCommentDetails> GetCommentAsync(AccountActor actor, Guid ticketId, Guid commentId, CancellationToken cancellationToken);
    Task<PagedResult<TicketActivityDetails>> ListActivitiesAsync(AccountActor actor, Guid ticketId, TicketDiscussionSearch search, CancellationToken cancellationToken);
    Task<TicketActivityDetails> GetActivityAsync(AccountActor actor, Guid ticketId, Guid activityId, CancellationToken cancellationToken);
}

public sealed class TicketDiscussionService(ITicketDiscussionStore store)
{
    public const int ContentLimit = 20_000;
    public Task<TicketCommentDetails> CreateAsync(AccountActor actor, Guid ticketId, CreateTicketComment command, CancellationToken cancellationToken)
    {
        if (ticketId == Guid.Empty || string.IsNullOrWhiteSpace(command.Content) || command.Content.Length > ContentLimit)
            throw new TicketOperationException(TicketError.InvalidInput);
        return store.CreateAsync(actor, ticketId, command with { Content = command.Content.Trim() }, cancellationToken);
    }
    public Task<PagedResult<TicketCommentDetails>> ListCommentsAsync(AccountActor actor, Guid ticketId, TicketDiscussionSearch search, CancellationToken cancellationToken)
    {
        Validate(ticketId, search);
        return store.ListCommentsAsync(actor, ticketId, search, cancellationToken);
    }
    public Task<PagedResult<TicketActivityDetails>> ListActivitiesAsync(AccountActor actor, Guid ticketId, TicketDiscussionSearch search, CancellationToken cancellationToken)
    {
        Validate(ticketId, search);
        return store.ListActivitiesAsync(actor, ticketId, search, cancellationToken);
    }
    public Task<TicketCommentDetails> GetCommentAsync(AccountActor actor, Guid ticketId, Guid commentId, CancellationToken cancellationToken) =>
        store.GetCommentAsync(actor, ticketId, commentId, cancellationToken);
    public Task<TicketActivityDetails> GetActivityAsync(AccountActor actor, Guid ticketId, Guid activityId, CancellationToken cancellationToken) =>
        store.GetActivityAsync(actor, ticketId, activityId, cancellationToken);
    private static void Validate(Guid ticketId, TicketDiscussionSearch search)
    {
        if (ticketId == Guid.Empty || search.Page is < 1 or > 1_000_000 || search.PageSize is < 1 or > 100)
            throw new TicketOperationException(TicketError.InvalidInput);
    }
}
