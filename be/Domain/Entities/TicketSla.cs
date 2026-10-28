using be.Domain.Enums;

namespace be.Domain.Entities;

public class TicketSla
{
    // Shared primary key with Ticket for the one-to-zero-or-one relationship.
    public Guid TicketId { get; set; }
    public Guid SlaPolicyId { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    public DateTime FirstResponseDueAt { get; set; }
    public DateTime ResolutionDueAt { get; set; }
    public DateTime? FirstRespondedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    // Nullable only for snapshots created before SLA MVP; never read the current policy as history.
    public string? PolicyName { get; set; }
    public TicketPriority? Priority { get; set; }
    public int? FirstResponseMinutes { get; set; }
    public int? ResolutionMinutes { get; set; }
    public DateTime? FirstResponseBreachedAt { get; set; }
    public DateTime? ResolutionBreachedAt { get; set; }
}
