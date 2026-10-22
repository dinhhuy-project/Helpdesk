using be.Application.Features.Auth;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Authentication;

public sealed class AuthUserStore(HelpdeskDbContext database) : IAuthUserStore
{
    public async Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        // Legacy rows may not be normalized. Ambiguous case variants fail closed instead of choosing an account.
        var matches = await database.Users.AsNoTracking()
            .Where(x => x.Email.ToLower() == normalizedEmail).Take(2).ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }

    public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        database.Users.AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken);

    public async Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking().Where(x => x.Id == userId && x.IsActive)
            .Select(x => new { x.Id, x.Email, x.DisplayName, x.AvatarUrl, x.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null) return null;

        var roles = await (from userRole in database.UserRoles
                           join role in database.Roles on userRole.RoleId equals role.Id
                           where userRole.UserId == userId
                           select role.Name).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        var permissions = await (from userRole in database.UserRoles
                                 join rolePermission in database.RolePermissions on userRole.RoleId equals rolePermission.RoleId
                                 join permission in database.Permissions on rolePermission.PermissionId equals permission.Id
                                 where userRole.UserId == userId
                                 select permission.Code).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        return new UserProfile(user.Id, user.Email, user.DisplayName, user.AvatarUrl, user.DepartmentId, roles, permissions);
    }

    public async Task<bool> UpdatePasswordHashAsync(Guid userId, string previousHash, string newHash, CancellationToken cancellationToken) =>
        await database.Users.Where(x => x.Id == userId && x.IsActive && x.PasswordHash == previousHash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PasswordHash, newHash)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), cancellationToken) == 1;
}
