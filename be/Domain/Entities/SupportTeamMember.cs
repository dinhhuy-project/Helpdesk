namespace be.Domain.Entities;

public class SupportTeamMember
{
    public Guid SupportTeamId { get; set; }
    public Guid UserId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public bool IsLeader { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public uint Version { get; set; }
}
