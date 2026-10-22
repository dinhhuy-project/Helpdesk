using Microsoft.AspNetCore.Authorization;

namespace be.Infrastructure.Authorization;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
