using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace be.Contracts.Requests;

public class AccountPageRequest
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class AccountSearchRequest : AccountPageRequest
{
    [StringLength(200)] public string? Search { get; init; }
    public bool? IsActive { get; init; }
    [RegularExpression("^(createdAt|email|displayName)$")] public string SortBy { get; init; } = "createdAt";
    public bool Descending { get; init; } = true;
}

public abstract class VersionedAccountRequest
{
    [Range(typeof(uint), "1", "4294967295")] public uint Version { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateAccountRequest
{
    [Required, EmailAddress, StringLength(320)] public string Email { get; init; } = "";
    [Required, StringLength(200, MinimumLength = 1)] public string DisplayName { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; init; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateAccountRequest : VersionedAccountRequest
{
    [Required, StringLength(200, MinimumLength = 1)] public string DisplayName { get; init; } = "";
    public Guid? DepartmentId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AccountStatusRequest : VersionedAccountRequest
{
    public required bool IsActive { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AccountRolesRequest : VersionedAccountRequest, IValidatableObject
{
    [Required, MaxLength(50)] public required Guid[] RoleIds { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RoleIds is not null && (RoleIds.Contains(Guid.Empty) || RoleIds.Distinct().Count() != RoleIds.Length))
            yield return new ValidationResult("Role IDs must be unique and non-empty.", [nameof(RoleIds)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RoleGrantsRequest : VersionedAccountRequest, IValidatableObject
{
    [Required, MaxLength(100)] public required Guid[] PermissionIds { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PermissionIds is not null && (PermissionIds.Contains(Guid.Empty) || PermissionIds.Distinct().Count() != PermissionIds.Length))
            yield return new ValidationResult("Permission IDs must be unique and non-empty.", [nameof(PermissionIds)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeAccountPasswordRequest
{
    [Required, StringLength(128)] public string CurrentPassword { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string NewPassword { get; init; } = "";
}
