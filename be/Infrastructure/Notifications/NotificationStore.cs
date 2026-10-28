using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Notifications;
using be.Application.Features.Tickets;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using be.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Notifications;

public sealed class NotificationStore(HelpdeskDbContext database, TicketScope scope, IAuthSessionStore sessions, TimeProvider clock) : INotificationStore
{
    private IQueryable<Notification> Visible(AccountActor actor)
    {
        var readable = scope.For(actor, TicketOperation.Read).Select(x => x.Id);
        var internalReadable = scope.For(actor, TicketOperation.ReadInternal).Select(x => x.Id);
        return database.Notifications.AsNoTracking().Where(notification => notification.UserId == actor.UserId && notification.TicketId != null &&
            readable.Contains(notification.TicketId.Value) && database.TicketActivities.Any(activity => activity.Id == notification.TicketActivityId &&
                activity.TicketId == notification.TicketId && (NotificationKinds.Public.Contains(activity.Action) ||
                (NotificationKinds.Internal.Contains(activity.Action) && internalReadable.Contains(activity.TicketId)))));
    }
    public Task<bool> CanReadAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) =>
        Visible(actor).AnyAsync(x => x.Id == id, cancellationToken);
    public async Task<PagedResult<NotificationDetails>> ListAsync(AccountActor actor, NotificationSearch search, CancellationToken cancellationToken)
    {
        await RequireSessionAsync(actor, cancellationToken);
        var query = Visible(actor);
        if (search.Unread is not null) query = query.Where(x => (x.ReadAt == null) == search.Unread);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(cancellationToken);
        return new(await DetailsAsync(rows, cancellationToken), search.Page, search.PageSize, count);
    }
    public async Task<int> UnreadCountAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        await RequireSessionAsync(actor, cancellationToken);
        return await Visible(actor).CountAsync(x => x.ReadAt == null, cancellationToken);
    }
    public async Task<NotificationDetails> GetAsync(AccountActor actor, Guid id, CancellationToken cancellationToken)
    {
        await RequireSessionAsync(actor, cancellationToken);
        var row = await Visible(actor).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new AccountOperationException(AccountError.NotFound);
        return (await DetailsAsync([row], cancellationToken))[0];
    }
    public async Task<NotificationDetails> MarkReadAsync(AccountActor actor, Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
        var current = await GetAsync(actor, id, cancellationToken);
        if (current.ReadAt is null)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await database.Notifications.Where(x => x.Id == id && x.UserId == actor.UserId && x.ReadAt == null).ExecuteUpdateAsync(set => set
                .SetProperty(x => x.ReadAt, now).SetProperty(x => x.DispatchedAt, (DateTime?)null)
                .SetProperty(x => x.DispatchClaimId, (Guid?)null).SetProperty(x => x.DispatchLeaseUntil, (DateTime?)null), cancellationToken);
        }
        var result = await GetAsync(actor, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
    private async Task RequireSessionAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw new AccountOperationException(AccountError.Unauthenticated);
    }
    private async Task<NotificationDetails[]> DetailsAsync(IReadOnlyList<Notification> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(x => x.TicketActivityId).ToArray();
        var actions = await database.TicketActivities.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Action, cancellationToken);
        return rows.Select(row => new NotificationDetails(row.Id, row.TicketId!.Value, row.TicketActivityId!.Value,
            actions[row.TicketActivityId.Value], NotificationKinds.Internal.Contains(actions[row.TicketActivityId.Value]),
            NotificationKinds.Title(actions[row.TicketActivityId.Value]), row.CreatedAt, row.ReadAt)).ToArray();
    }
}
