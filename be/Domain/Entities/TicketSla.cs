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
}
