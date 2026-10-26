using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace be.Contracts.Requests;

public class DepartmentLookupRequest : AccountPageRequest
{
    [StringLength(200)] public string? Search { get; init; }
}
public sealed class DepartmentSearchRequest : DepartmentLookupRequest
{
    public bool? IsActive { get; init; }
    [RegularExpression("^(name|createdAt)$")] public string SortBy { get; init; } = "name";
    public bool Descending { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateDepartmentRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateDepartmentRequest : VersionedAccountRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DepartmentStatusRequest : VersionedAccountRequest
{
    public required bool IsActive { get; init; }
}
