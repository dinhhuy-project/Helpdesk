namespace be.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public string NormalizedEmail { get; private set; } = "";
    public required string DisplayName { get; set; }
    // Null for accounts that authenticate only through an external provider.
    public string? PasswordHash { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public uint Version { get; set; }
}
