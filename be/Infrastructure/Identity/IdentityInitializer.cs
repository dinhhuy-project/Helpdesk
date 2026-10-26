using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Identity;

public sealed class IdentityInitializer(HelpdeskDbContext database, IPasswordHasher<User> passwordHasher)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await LockAndCheckMigrationsAsync(cancellationToken);
        var permissions = await database.Permissions.ToDictionaryAsync(x => x.Code, cancellationToken);
        foreach (var code in IdentitySeedCatalog.Permissions)
        {
            if (permissions.ContainsKey(code)) continue;
            var permission = new Permission { Code = code };
            permissions.Add(code, permission);
            database.Permissions.Add(permission);
        }

        var existingRoles = await database.Roles.Select(x => x.Name).ToListAsync(cancellationToken);
        foreach (var (name, codes) in IdentitySeedCatalog.Roles)
        {
            // Existing roles keep their current grants, including intentionally removed grants.
            if (existingRoles.Contains(name, StringComparer.Ordinal)) continue;
            var role = new Role { Name = name };
            database.Roles.Add(role);
            foreach (var code in codes)
                database.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissions[code].Id });
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // True only when a new account was created. Never modifies an existing account.
    public async Task<bool> BootstrapAdminAsync(BootstrapAdminOptions options, CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await LockAndCheckMigrationsAsync(cancellationToken);
        var adminRole = await database.Roles.SingleOrDefaultAsync(x => x.Name == RoleNames.SystemAdmin, cancellationToken)
            ?? throw new IdentityInitializationException("Run seed-identity before bootstrap-admin.");

        // The command only bootstraps the first administrator, including inactive existing admins.
        if (await database.UserRoles.AnyAsync(x => x.RoleId == adminRole.Id, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        options.Validate();
        var email = options.Email.Trim().ToLowerInvariant();
        if (await database.Users.AnyAsync(x => x.NormalizedEmail == email, cancellationToken))
            throw new IdentityInitializationException("The bootstrap email already belongs to an account. No account was changed.");

        var user = new User { Email = email, DisplayName = options.DisplayName.Trim() };
        user.PasswordHash = passwordHasher.HashPassword(user, options.Password);
        database.Users.Add(user);
        database.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.Id });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task GrantAccountAdministrationAsync(CancellationToken cancellationToken = default)
        => await GrantAdministrationAsync([PermissionCodes.UserManage, PermissionCodes.RoleManage], "AccountAdministrationEnabled", cancellationToken);

    public async Task GrantDepartmentAdministrationAsync(CancellationToken cancellationToken = default)
        => await GrantAdministrationAsync([PermissionCodes.DepartmentManage], "DepartmentAdministrationEnabled", cancellationToken);

    private async Task GrantAdministrationAsync(string[] codes, string action, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await LockAndCheckMigrationsAsync(cancellationToken);
        var role = await database.Roles.SingleOrDefaultAsync(x => x.Name == RoleNames.SystemAdmin, cancellationToken)
            ?? throw new IdentityInitializationException("Run seed-identity first.");
        var permissions = await database.Permissions.Where(x => codes.Contains(x.Code)).ToListAsync(cancellationToken);
        if (permissions.Count != codes.Length) throw new IdentityInitializationException("Run seed-identity first.");
        var existing = await database.RolePermissions.Where(x => x.RoleId == role.Id).Select(x => x.PermissionId).ToListAsync(cancellationToken);
        var missing = permissions.Where(x => !existing.Contains(x.Id)).ToArray();
        if (missing.Length > 0)
        {
            database.RolePermissions.AddRange(missing.Select(x => new RolePermission { RoleId = role.Id, PermissionId = x.Id }));
            role.UpdatedAt = DateTime.UtcNow;
            database.IdentityAuditLogs.Add(new IdentityAuditLog { Action = action, TargetRoleId = role.Id,
                Data = System.Text.Json.JsonSerializer.Serialize(new { Permissions = missing.Select(x => x.Code).ToArray() }) });
            await database.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task LockAndCheckMigrationsAsync(CancellationToken cancellationToken)
    {
        // Shared transaction lock serializes both initializer commands across application instances.
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
        if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            throw new IdentityInitializationException("Apply reviewed migrations before running Identity initialization.");
    }
}
