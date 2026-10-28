using be.Application.Features.Accounts;

namespace be.Application.Features.Notifications;

public sealed record NotificationDetails(Guid Id, Guid TicketId, Guid ActivityId, string Kind, bool IsInternal,
    string Title, DateTime CreatedAt, DateTime? ReadAt);
public sealed record NotificationSearch(int Page = 1, int PageSize = 20, bool? Unread = null);
public sealed record NotificationUnreadCount(int Count);
public interface INotificationStore
{
    Task<PagedResult<NotificationDetails>> ListAsync(AccountActor actor, NotificationSearch search, CancellationToken cancellationToken);
    Task<int> UnreadCountAsync(AccountActor actor, CancellationToken cancellationToken);
    Task<NotificationDetails> GetAsync(AccountActor actor, Guid id, CancellationToken cancellationToken);
    Task<NotificationDetails> MarkReadAsync(AccountActor actor, Guid id, CancellationToken cancellationToken);
}
public sealed class NotificationService(INotificationStore store)
{
    public Task<PagedResult<NotificationDetails>> ListAsync(AccountActor actor, NotificationSearch search, CancellationToken cancellationToken)
    {
        if (search.Page is < 1 or > 1_000_000 || search.PageSize is < 1 or > 100) throw new AccountOperationException(AccountError.InvalidInput);
        return store.ListAsync(actor, search, cancellationToken);
    }
    public Task<int> UnreadCountAsync(AccountActor actor, CancellationToken cancellationToken) => store.UnreadCountAsync(actor, cancellationToken);
    public Task<NotificationDetails> GetAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) => store.GetAsync(actor, id, cancellationToken);
    public Task<NotificationDetails> MarkReadAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) => store.MarkReadAsync(actor, id, cancellationToken);
}
