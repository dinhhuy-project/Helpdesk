using be.Domain.Entities;
using be.Domain.Enums;

namespace be.Domain.Rules;

public static class SlaRules
{
    public const int MaximumMinutes = 525_600;
    public static bool ValidDurations(int response, int resolution) => response > 0 && response <= resolution && resolution <= MaximumMinutes;

    public static TicketSla Snapshot(Ticket ticket, SlaPolicy policy)
    {
        if (ticket.CreatedAt.Kind != DateTimeKind.Utc || ticket.Priority != policy.Priority || !policy.IsActive ||
            !ValidDurations(policy.FirstResponseMinutes, policy.ResolutionMinutes))
            throw new InvalidOperationException("An active, valid SLA policy and a UTC Ticket are required.");
        return new TicketSla { TicketId = ticket.Id, SlaPolicyId = policy.Id, PolicyName = policy.Name, Priority = policy.Priority,
            FirstResponseMinutes = policy.FirstResponseMinutes, ResolutionMinutes = policy.ResolutionMinutes, AppliedAt = ticket.CreatedAt,
            FirstResponseDueAt = ticket.CreatedAt.AddMinutes(policy.FirstResponseMinutes), ResolutionDueAt = ticket.CreatedAt.AddMinutes(policy.ResolutionMinutes) };
    }

    public static bool FirstResponseBreached(TicketSla sla, TicketStatus status, DateTime now) => sla.FirstResponseBreachedAt != null ||
        (status is not (TicketStatus.Closed or TicketStatus.Cancelled) && sla.FirstResponseDueAt < FirstResponseEnd(sla, now));
    public static bool ResolutionBreached(TicketSla sla, TicketStatus status, DateTime now) => sla.ResolutionBreachedAt != null ||
        (status is not (TicketStatus.Closed or TicketStatus.Cancelled) && sla.ResolutionDueAt < (sla.ResolvedAt ?? now));

    // Resolve stops both clocks; only a qualifying public comment fills FirstRespondedAt.
    // Reopening clears ResolvedAt, resumes the original deadlines and retains breach history.
    private static DateTime FirstResponseEnd(TicketSla sla, DateTime now)
    {
        var end = sla.ResolvedAt ?? now;
        return sla.FirstRespondedAt is { } response && response < end ? response : end;
    }
}
