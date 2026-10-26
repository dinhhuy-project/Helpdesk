namespace be.Domain.Entities;

public class IdentityAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorId { get; set; }
    public Guid? TargetUserId { get; set; }
    public Guid? TargetRoleId { get; set; }
    public required string Action { get; set; }
    // Only explicitly selected non-secret metadata belongs here.
    public string? Data { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
