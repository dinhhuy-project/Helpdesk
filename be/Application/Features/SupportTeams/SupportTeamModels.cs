namespace be.Application.Features.SupportTeams;

public sealed record SupportTeamDetails(Guid Id, Guid DepartmentId, string Name, string? Description,
    bool IsActive, bool DepartmentIsActive, DateTime CreatedAt, DateTime? UpdatedAt, uint Version, string DepartmentName);
public sealed record TeamMemberDetails(Guid UserId, string DisplayName, bool UserIsActive, bool IsActive,
    bool IsLeader, DateTime JoinedAt, DateTime? UpdatedAt, uint Version, bool CanBeLeader);
public sealed record TeamAdministrationAccess(bool CanCreate, bool CanChangeStatus, bool CanManageLeadership, bool CanAddMembers);
public sealed record TeamMemberCandidate(Guid Id, string DisplayName, string? DepartmentName);
public sealed record TeamSearch(int Page = 1, int PageSize = 20, string? Search = null,
    Guid? DepartmentId = null, bool? IsActive = null, string SortBy = "name", bool Descending = false);
public sealed record TeamMemberSearch(int Page = 1, int PageSize = 20, bool? IsActive = null);
public sealed record TeamLookupSearch(int Page = 1, int PageSize = 20, string? Search = null);
public sealed record CreateSupportTeam(Guid DepartmentId, string Name, string? Description);
public sealed record UpdateSupportTeam(string Name, string? Description, uint Version);
public sealed record SupportTeamStatus(bool IsActive, uint Version);
public sealed record AddTeamMember(Guid UserId, uint TeamVersion);
public sealed record TeamMemberStatus(bool IsActive, uint Version, uint TeamVersion);
public sealed record TeamMemberLeadership(bool IsLeader, uint Version, uint TeamVersion);
public sealed record TeamMembershipResult(SupportTeamDetails Team, TeamMemberDetails Member);

public enum TeamError
{
    NotFound, NameInUse, VersionConflict, InvalidInput, InvalidDepartment, InactiveTeam,
    InvalidUser, MembershipExists, InvalidLeader, Unauthenticated, InsufficientScope
}
public sealed class TeamOperationException(TeamError error) : Exception("Support team operation failed.")
{
    public TeamError Error { get; } = error;
}
