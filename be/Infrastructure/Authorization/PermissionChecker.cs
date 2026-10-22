using be.Application.Common.Authorization;
using be.Domain.Constants;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Authorization;

public sealed class PermissionChecker(HelpdeskDbContext database, TimeProvider timeProvider) : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(Guid userId, Guid sessionId, string permission, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty || !PermissionCodes.All.Contains(permission, StringComparer.Ordinal))
            return Task.FromResult(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        // A fresh query for each decision: neither JWT claims nor a role name grants permission by itself.
        return (from session in database.RefreshTokens
                join user in database.Users on session.UserId equals user.Id
                join userRole in database.UserRoles on user.Id equals userRole.UserId
                join rolePermission in database.RolePermissions on userRole.RoleId equals rolePermission.RoleId
                join grant in database.Permissions on rolePermission.PermissionId equals grant.Id
                where session.Id == sessionId && user.Id == userId && user.IsActive &&
                      session.RevokedAt == null && session.ExpiresAt > now && grant.Code == permission
                select grant.Id).AnyAsync(cancellationToken);
    }
}
