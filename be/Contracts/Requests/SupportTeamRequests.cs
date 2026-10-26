using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace be.Contracts.Requests;

public sealed class SupportTeamSearchRequest : AccountPageRequest
{
    [StringLength(200)] public string? Search { get; init; }
    public Guid? DepartmentId { get; init; }
    public bool? IsActive { get; init; }
    [RegularExpression("^(name|createdAt)$")] public string SortBy { get; init; } = "name";
    public bool Descending { get; init; }
}
public sealed class TeamMemberSearchRequest : AccountPageRequest
{
    public bool? IsActive { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateSupportTeamRequest
{
    public required Guid DepartmentId { get; init; }
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateSupportTeamRequest : VersionedAccountRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SupportTeamStatusRequest : VersionedAccountRequest
{
    public required bool IsActive { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AddTeamMemberRequest
{
    public required Guid UserId { get; init; }
    [Range(typeof(uint), "1", "4294967295")] public uint TeamVersion { get; init; }
}
public class TeamMembershipVersionRequest : VersionedAccountRequest
{
    [Range(typeof(uint), "1", "4294967295")] public uint TeamVersion { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TeamMemberStatusRequest : TeamMembershipVersionRequest
{
    public required bool IsActive { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TeamMemberLeadershipRequest : TeamMembershipVersionRequest
{
    public required bool IsLeader { get; init; }
}
