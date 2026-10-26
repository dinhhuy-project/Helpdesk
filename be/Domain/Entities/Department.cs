namespace be.Domain.Entities;

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string NormalizedName { get; private set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public uint Version { get; set; }
}
