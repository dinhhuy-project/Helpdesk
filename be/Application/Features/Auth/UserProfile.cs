namespace be.Application.Features.Auth;

public sealed record UserProfile(
    Guid Id, string Email, string DisplayName, string? AvatarUrl, Guid? DepartmentId,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
