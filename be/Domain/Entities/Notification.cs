namespace be.Domain.Entities;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? TicketActivityId { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
    // Durable dispatch state; claiming never grants permission to deliver.
    public DateTime? DispatchedAt { get; set; }
    public Guid? DispatchClaimId { get; set; }
    public DateTime? DispatchLeaseUntil { get; set; }
}
