using be.Domain.Enums;

namespace be.Domain.Entities;

public class SlaPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public int FirstResponseMinutes { get; set; }
    public int ResolutionMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public uint Version { get; set; }
}
