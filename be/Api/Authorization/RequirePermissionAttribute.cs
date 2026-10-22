using be.Domain.Constants;
using Microsoft.AspNetCore.Authorization;

namespace be.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission)
    {
        if (!PermissionCodes.All.Contains(permission, StringComparer.Ordinal))
            throw new ArgumentException("Use a registered PermissionCodes value.", nameof(permission));
        Policy = permission;
    }
}
