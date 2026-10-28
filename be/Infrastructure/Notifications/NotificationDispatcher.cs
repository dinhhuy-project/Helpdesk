using be.Application.Features.Auth;
using be.Configuration;
using be.Infrastructure.Persistence;
using be.Infrastructure.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace be.Infrastructure.Notifications;

public sealed class NotificationDispatcher(HelpdeskDbContext database, NotificationStore notifications, RealtimeConnections connections,
    IAuthSessionStore sessions, IRealtimeSender sender, TimeProvider clock, IOptions<NotificationOptions> options, ILogger<NotificationDispatcher> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        var now = clock.GetUtcNow().UtcDateTime;
        var claimId = Guid.NewGuid();
        Guid[] ids;
        await using (var transaction = await database.Database.BeginTransactionAsync(deadline.Token))
        {
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", deadline.Token);
            var rows = await database.Notifications.Where(x => x.TicketActivityId != null && x.DispatchedAt == null &&
                (x.DispatchLeaseUntil == null || x.DispatchLeaseUntil <= now))
                .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(options.Value.BatchSize).ToListAsync(deadline.Token);
            foreach (var row in rows) { row.DispatchClaimId = claimId; row.DispatchLeaseUntil = now.AddMinutes(1); }
            await database.SaveChangesAsync(deadline.Token);
            await transaction.CommitAsync(deadline.Token);
            ids = rows.Select(x => x.Id).ToArray();
        }
        var completed = 0;
        foreach (var id in ids)
        {
            if (deadline.IsCancellationRequested) break;
            try
            {
                var row = await database.Notifications.AsNoTracking().SingleAsync(x => x.Id == id, deadline.Token);
                foreach (var connection in connections.Snapshot().Where(x => x.Actor.UserId == row.UserId))
                {
                    if (!await CanSendAsync(connection, id, deadline.Token)) continue;
                    // SignalR I/O occurs after claim commit, never inside a database transaction.
                    await sender.SendAsync(connection.Id, "NotificationChanged", row.Id, deadline.Token);
                    if (row.TicketId is { } ticketId && connection.IsWatching(ticketId) && await CanSendAsync(connection, id, deadline.Token))
                        await sender.SendAsync(connection.Id, "TicketChanged", ticketId, deadline.Token);
                }
                var processedAt = clock.GetUtcNow().UtcDateTime;
                completed += await database.Notifications.Where(x => x.Id == id && x.DispatchClaimId == claimId).ExecuteUpdateAsync(set => set
                    .SetProperty(x => x.DispatchedAt, processedAt).SetProperty(x => x.DispatchClaimId, (Guid?)null)
                    .SetProperty(x => x.DispatchLeaseUntil, (DateTime?)null), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                var retryAt = clock.GetUtcNow().UtcDateTime.AddSeconds(5);
                await database.Notifications.Where(x => x.Id == id && x.DispatchClaimId == claimId).ExecuteUpdateAsync(set => set
                    .SetProperty(x => x.DispatchLeaseUntil, retryAt), cancellationToken);
                logger.LogWarning("Notification dispatch failed ({ExceptionType}); notification {NotificationId} will be retried.", error.GetType().Name, id);
            }
        }
        return completed;
    }
    private async Task<bool> CanSendAsync(RealtimeConnection connection, Guid id, CancellationToken cancellationToken)
    {
        if (connection.ExpiresAt <= clock.GetUtcNow().UtcDateTime ||
            !await sessions.IsActiveAsync(connection.Actor.UserId, connection.Actor.SessionId, cancellationToken))
        { connections.Remove(connection.Id); connection.Abort(); return false; }
        return await notifications.CanReadAsync(connection.Actor, id, cancellationToken);
    }
}
