namespace be.Domain.Entities;

public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public string? Description { get; set; }
}
