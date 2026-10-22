namespace be.Domain.Entities;

public class ExternalIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string Provider { get; set; }
    public required string Subject { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
