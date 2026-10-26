using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace be.Contracts.Requests;

public class CategoryLookupRequest : AccountPageRequest
{
    [StringLength(200)] public string? Search { get; init; }
    public Guid? ParentId { get; init; }
    public bool RootsOnly { get; init; }
}
public sealed class CategorySearchRequest : CategoryLookupRequest
{
    public bool? IsActive { get; init; }
    [RegularExpression("^(name|createdAt)$")] public string SortBy { get; init; } = "name";
    public bool Descending { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTicketCategoryRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
    public Guid? ParentId { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateTicketCategoryRequest : VersionedAccountRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
    // Explicit null moves a node to root; omission must not silently reparent a subtree.
    public required Guid? ParentId { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TicketCategoryStatusRequest : VersionedAccountRequest
{
    public required bool IsActive { get; init; }
}
