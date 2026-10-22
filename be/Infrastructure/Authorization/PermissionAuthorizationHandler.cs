using System.IdentityModel.Tokens.Jwt;
using be.Application.Common.Authorization;
using be.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace be.Infrastructure.Authorization;

public sealed class PermissionAuthorizationHandler(IPermissionChecker permissions, IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) || userId == Guid.Empty ||
            !Guid.TryParse(context.User.FindFirst(JwtAccessTokenIssuer.SessionIdClaim)?.Value, out var sessionId) || sessionId == Guid.Empty)
            return;

        var cancellationToken = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        if (await permissions.HasPermissionAsync(userId, sessionId, requirement.Permission, cancellationToken))
            context.Succeed(requirement);
    }
}
