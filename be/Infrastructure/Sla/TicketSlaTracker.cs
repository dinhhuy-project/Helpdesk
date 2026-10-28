using System.Text.Json;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Domain.Rules;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Sla;

// Caller holds the shared mutation lock and the Ticket row lock (or is creating the Ticket).
public sealed class TicketSlaTracker(HelpdeskDbContext database)
{
    public async Task ApplyAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var policy = await database.SlaPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.Priority == ticket.Priority, cancellationToken);
        if (policy is not null) database.TicketSlas.Add(SlaRules.Snapshot(ticket, policy));
    }
    public async Task FirstResponseAsync(Ticket ticket, Guid actorId, DateTime now, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var sla = await database.TicketSlas.SingleOrDefaultAsync(x => x.TicketId == ticket.Id, cancellationToken);
        if (sla is null || ticket.Status is TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Cancelled) return;
        sla.FirstRespondedAt ??= now;
        Detect(ticket, sla, now, actorId);
    }
    public async Task TransitionAsync(Ticket ticket, DateTime now, Guid actorId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var sla = await database.TicketSlas.SingleOrDefaultAsync(x => x.TicketId == ticket.Id, cancellationToken);
        if (sla is null) return;
        sla.ResolvedAt = ticket.ResolvedAt;
        Detect(ticket, sla, now, actorId);
    }
    public void Detect(Ticket ticket, TicketSla sla, DateTime now, Guid? actorId = null)
    {
        RequireTransaction();
        if (ticket.Status is TicketStatus.Closed or TicketStatus.Cancelled) return;
        if (sla.FirstResponseBreachedAt is null && SlaRules.FirstResponseBreached(sla, ticket.Status, now))
        {
            sla.FirstResponseBreachedAt = now;
            Activity(TicketActivityActions.SlaFirstResponseBreached, sla.FirstResponseDueAt);
        }
        if (sla.ResolutionBreachedAt is null && SlaRules.ResolutionBreached(sla, ticket.Status, now))
        {
            sla.ResolutionBreachedAt = now;
            Activity(TicketActivityActions.SlaResolutionBreached, sla.ResolutionDueAt);
        }
        void Activity(string action, DateTime dueAt) => database.TicketActivities.Add(new TicketActivity
        { TicketId = ticket.Id, ActorId = actorId, Action = action, CreatedAt = now, NewValue = JsonSerializer.Serialize(new { DueAt = dueAt, DetectedAt = now }) });
    }
    private void RequireTransaction()
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("SLA changes require the Ticket mutation transaction.");
    }
}
