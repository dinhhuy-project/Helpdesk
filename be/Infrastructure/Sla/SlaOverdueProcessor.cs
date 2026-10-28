using be.Configuration;
using be.Domain.Enums;
using be.Infrastructure.Notifications;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace be.Infrastructure.Sla;

public sealed class SlaOverdueProcessor(HelpdeskDbContext database, TicketSlaTracker tracker, TicketNotificationWriter notifications,
    TimeProvider clock, IOptions<SlaOptions> options)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        // Resolved timestamps bound the clocks, even if the worker was offline during resolution.
        var due = from sla in database.TicketSlas
                  join ticket in database.Tickets on sla.TicketId equals ticket.Id
                  where ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled &&
                      ((sla.FirstResponseBreachedAt == null && sla.FirstResponseDueAt < (sla.FirstRespondedAt ?? sla.ResolvedAt ?? now) &&
                        (sla.ResolvedAt == null || sla.FirstResponseDueAt < sla.ResolvedAt)) ||
                       (sla.ResolutionBreachedAt == null && sla.ResolutionDueAt < (sla.ResolvedAt ?? now)))
                  orderby sla.ResolutionDueAt, sla.TicketId select ticket.Id;
        var ids = await due.Take(options.Value.BatchSize).ToArrayAsync(cancellationToken);
        foreach (var id in ids)
        {
            var ticket = await database.Tickets.FromSqlInterpolated($"SELECT *, xmin FROM helpdesk.tickets WHERE id = {id} FOR UPDATE").SingleAsync(cancellationToken);
            var sla = await database.TicketSlas.SingleAsync(x => x.TicketId == id, cancellationToken);
            tracker.Detect(ticket, sla, now);
        }
        await notifications.SaveChangesAsync(Guid.Empty, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ids.Length;
    }
}
