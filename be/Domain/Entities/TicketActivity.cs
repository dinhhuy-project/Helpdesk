namespace be.Domain.Entities;

public class TicketActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    // Null for automated system actions.
    public Guid? ActorId { get; set; }
    public required string Action { get; set; }
    // JSON payloads; jsonb mapping belongs to Infrastructure.
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
