namespace be.Application.Features.Accounts;

public sealed record AccountActor(Guid UserId, Guid SessionId);
public sealed record AccountRole(Guid Id, string Name);
public sealed record AccountUser(Guid Id, string Email, string DisplayName, bool IsActive, Guid? DepartmentId,
    DateTime CreatedAt, DateTime? UpdatedAt, uint Version, IReadOnlyList<AccountRole> Roles);
public sealed record AccountRoleDetails(Guid Id, string Name, string? Description, uint Version, IReadOnlyList<string> Permissions);
public sealed record AccountPermission(Guid Id, string Code);
public sealed record AccountAudit(Guid Id, Guid? ActorId, Guid? TargetUserId, Guid? TargetRoleId, string Action, string? Data, DateTime CreatedAt);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record AccountSearch(int Page = 1, int PageSize = 20, string? Search = null,
    bool? IsActive = null, string SortBy = "createdAt", bool Descending = true);
public sealed record CreateAccount(string Email, string DisplayName, string Password);
public sealed record UpdateAccount(string DisplayName, Guid? DepartmentId, uint Version);
public sealed record AccountStatus(bool IsActive, uint Version);
public sealed record AccountRoles(IReadOnlyList<Guid> RoleIds, uint Version);
public sealed record RoleGrants(IReadOnlyList<Guid> PermissionIds, uint Version);
public sealed record ChangeAccountPassword(string CurrentPassword, string NewPassword);
