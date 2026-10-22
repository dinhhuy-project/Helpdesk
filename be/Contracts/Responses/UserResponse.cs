using be.Application.Features.Auth;

namespace be.Contracts.Responses;

public sealed record UserResponse(
    Guid Id, string Email, string DisplayName, string? AvatarUrl, Guid? DepartmentId,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)
{
    public static UserResponse FromProfile(UserProfile profile) => new(
        profile.Id, profile.Email, profile.DisplayName, profile.AvatarUrl,
        profile.DepartmentId, profile.Roles, profile.Permissions);
}
