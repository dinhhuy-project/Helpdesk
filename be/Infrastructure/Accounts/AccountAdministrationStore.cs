using System.Text.Json;
using be.Application.Common.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace be.Infrastructure.Accounts;

public sealed class AccountAdministrationStore(HelpdeskDbContext database, IPermissionChecker permissions,
    IAuthSessionStore sessions, IPasswordHasher<User> passwordHasher, TimeProvider clock) : IAccountAdministrationStore
{
    public async Task<PagedResult<AccountUser>> ListAsync(AccountSearch search, CancellationToken cancellationToken)
    {
        var query = database.Users.AsNoTracking();
        if (search.IsActive is not null) query = query.Where(x => x.IsActive == search.IsActive);
        if (!string.IsNullOrEmpty(search.Search))
            query = query.Where(x => x.NormalizedEmail.Contains(search.Search) || x.DisplayName.ToLower().Contains(search.Search));
        var count = await query.CountAsync(cancellationToken);
        var ordered = (search.SortBy, search.Descending) switch
        {
            ("email", false) => query.OrderBy(x => x.NormalizedEmail),
            ("email", true) => query.OrderByDescending(x => x.NormalizedEmail),
            ("displayName", false) => query.OrderBy(x => x.DisplayName),
            ("displayName", true) => query.OrderByDescending(x => x.DisplayName),
            (_, false) => query.OrderBy(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.CreatedAt)
        };
        var items = await Project(ordered.ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize))
            .ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<AccountUser> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await Project(database.Users.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(cancellationToken)
        ?? throw Error(AccountError.NotFound);

    public async Task<AccountUser> CreateAsync(AccountActor actor, CreateAccount command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, PermissionCodes.UserManage, cancellationToken);
        if (await database.Users.AnyAsync(x => x.NormalizedEmail == command.Email, cancellationToken)) throw Error(AccountError.EmailInUse);
        var requester = await database.Roles.SingleOrDefaultAsync(x => x.Name == RoleNames.Requester, cancellationToken)
            ?? throw Error(AccountError.InvalidRole);
        var user = new User { Email = command.Email, DisplayName = command.DisplayName, CreatedAt = Now };
        user.PasswordHash = passwordHasher.HashPassword(user, command.Password);
        database.Users.Add(user);
        database.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = requester.Id });
        Audit(actor, "UserCreated", user.Id, null, new { DefaultRoleId = requester.Id });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(user.Id, cancellationToken);
    }

    public async Task<AccountUser> UpdateAsync(AccountActor actor, Guid id, UpdateAccount command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, PermissionCodes.UserManage, cancellationToken);
        var user = await LockUserAsync(id, cancellationToken);
        await CheckTargetScopeAsync(actor, user.Id, cancellationToken);
        CheckVersion(user.Version, command.Version);
        // Department administration uses the same advisory lock. Keep historical assignments
        // when editing other fields, but require an active department for any new assignment.
        if (command.DepartmentId is not null && command.DepartmentId != user.DepartmentId && !await database.Departments.AnyAsync(
                x => x.Id == command.DepartmentId && x.IsActive, cancellationToken)) throw Error(AccountError.InvalidDepartment);
        if (user.DisplayName != command.DisplayName || user.DepartmentId != command.DepartmentId)
        {
            user.DisplayName = command.DisplayName;
            user.DepartmentId = command.DepartmentId;
            user.UpdatedAt = Now;
            Audit(actor, "UserUpdated", id, null, new { Fields = new[] { "displayName", "departmentId" } });
            await database.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<AccountUser> SetStatusAsync(AccountActor actor, Guid id, AccountStatus command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, PermissionCodes.UserManage, cancellationToken);
        var user = await LockUserAsync(id, cancellationToken);
        await CheckTargetScopeAsync(actor, id, cancellationToken);
        CheckVersion(user.Version, command.Version);
        if (user.IsActive != command.IsActive)
        {
            if (!command.IsActive && await IsAdminAsync(id, cancellationToken)) await CheckOtherAdminAsync(id, cancellationToken);
            var previous = user.IsActive;
            user.IsActive = command.IsActive;
            user.UpdatedAt = Now;
            await RevokeSessionsAsync(id, cancellationToken);
            Audit(actor, "UserStatusChanged", id, null, new { Previous = previous, Current = user.IsActive });
            await database.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<AccountUser> SetRolesAsync(AccountActor actor, Guid id, AccountRoles command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, PermissionCodes.RoleManage, cancellationToken);
        if (id == actor.UserId) throw Error(AccountError.InsufficientScope);
        var user = await LockUserAsync(id, cancellationToken);
        CheckVersion(user.Version, command.Version);
        var requested = command.RoleIds.Distinct().ToArray();
        var roles = await database.Roles.Where(x => requested.Contains(x.Id)).ToListAsync(cancellationToken);
        if (roles.Count != command.RoleIds.Count || requested.Contains(Guid.Empty)) throw Error(AccountError.InvalidRole);
        var previous = await database.UserRoles.Where(x => x.UserId == id).Select(x => x.RoleId).ToArrayAsync(cancellationToken);
        var affected = previous.Union(requested).ToArray();
        if (!await IsAdminAsync(actor.UserId, cancellationToken))
        {
            var actorGrants = await UserGrants(actor.UserId).ToListAsync(cancellationToken);
            var affectedGrants = await database.RolePermissions.Where(x => affected.Contains(x.RoleId))
                .Join(database.Permissions, x => x.PermissionId, x => x.Id, (_, permission) => permission.Code)
                .Distinct().ToListAsync(cancellationToken);
            if (roles.Any(x => x.Name == RoleNames.SystemAdmin) || await IsAdminAsync(id, cancellationToken) ||
                affectedGrants.Any(x => x is PermissionCodes.UserManage or PermissionCodes.RoleManage || !actorGrants.Contains(x)))
                throw Error(AccountError.InsufficientScope);
        }
        if (user.IsActive && await IsAdminAsync(id, cancellationToken) && !roles.Any(x => x.Name == RoleNames.SystemAdmin))
            await CheckOtherAdminAsync(id, cancellationToken);
        if (!previous.ToHashSet().SetEquals(requested))
        {
            await database.UserRoles.Where(x => x.UserId == id).ExecuteDeleteAsync(cancellationToken);
            database.UserRoles.AddRange(requested.Select(roleId => new UserRole { UserId = id, RoleId = roleId }));
            user.UpdatedAt = Now; // Change the aggregate's xmin when membership changes.
            Audit(actor, "UserRolesChanged", id, null, new { Previous = previous, Current = requested });
            await database.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task ChangePasswordAsync(AccountActor actor, ChangeAccountPassword command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, null, cancellationToken);
        var user = await LockUserAsync(actor.UserId, cancellationToken);
        if (string.IsNullOrEmpty(user.PasswordHash)) throw Error(AccountError.PasswordlessAccount);
        PasswordVerificationResult verified;
        try { verified = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.CurrentPassword); }
        catch (FormatException) { throw Error(AccountError.InvalidCurrentPassword); }
        if (verified == PasswordVerificationResult.Failed || command.CurrentPassword == command.NewPassword)
            throw Error(AccountError.InvalidCurrentPassword);
        user.PasswordHash = passwordHasher.HashPassword(user, command.NewPassword);
        user.UpdatedAt = Now;
        await RevokeSessionsAsync(user.Id, cancellationToken);
        Audit(actor, "PasswordChanged", user.Id, null, null);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccountRoleDetails>> RolesAsync(CancellationToken cancellationToken) =>
        await ProjectRoles(database.Roles.AsNoTracking().OrderBy(x => x.Name)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AccountPermission>> PermissionsAsync(CancellationToken cancellationToken) =>
        await database.Permissions.AsNoTracking().Where(x => PermissionCodes.All.Contains(x.Code)).OrderBy(x => x.Code)
            .Select(x => new AccountPermission(x.Id, x.Code)).ToListAsync(cancellationToken);

    public async Task<AccountRoleDetails> SetGrantsAsync(AccountActor actor, Guid id, RoleGrants command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, PermissionCodes.RoleManage, cancellationToken);
        if (!await IsAdminAsync(actor.UserId, cancellationToken)) throw Error(AccountError.InsufficientScope);
        var role = await database.Roles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw Error(AccountError.NotFound);
        CheckVersion(role.Version, command.Version);
        var requested = command.PermissionIds.Distinct().ToArray();
        var grants = await database.Permissions.Where(x => requested.Contains(x.Id) && PermissionCodes.All.Contains(x.Code))
            .ToListAsync(cancellationToken);
        if (grants.Count != command.PermissionIds.Count) throw Error(AccountError.InvalidRole);
        if (role.Name == RoleNames.SystemAdmin &&
            (!grants.Any(x => x.Code == PermissionCodes.UserManage) || !grants.Any(x => x.Code == PermissionCodes.RoleManage)))
            throw Error(AccountError.ProtectedAdminGrant);
        var previous = await database.RolePermissions.Where(x => x.RoleId == id).Select(x => x.PermissionId).ToArrayAsync(cancellationToken);
        if (!previous.ToHashSet().SetEquals(requested))
        {
            await database.RolePermissions.Where(x => x.RoleId == id).ExecuteDeleteAsync(cancellationToken);
            database.RolePermissions.AddRange(requested.Select(permissionId => new RolePermission { RoleId = id, PermissionId = permissionId }));
            role.UpdatedAt = Now;
            Audit(actor, "RoleGrantsChanged", null, id, new { Previous = previous, Current = requested });
            await database.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await ProjectRoles(database.Roles.AsNoTracking().Where(x => x.Id == id)).SingleAsync(cancellationToken);
    }

    public async Task<PagedResult<AccountAudit>> AuditAsync(AccountActor actor, int page, int pageSize, CancellationToken cancellationToken)
    {
        await RequireActorAsync(actor, PermissionCodes.RoleManage, cancellationToken);
        if (!await IsAdminAsync(actor.UserId, cancellationToken)) throw Error(AccountError.InsufficientScope);
        var query = database.IdentityAuditLogs.AsNoTracking();
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AccountAudit(x.Id, x.ActorId, x.TargetUserId, x.TargetRoleId, x.Action, x.Data, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, count);
    }

    private IQueryable<AccountUser> Project(IQueryable<User> query) => query.Select(user => new AccountUser(
        user.Id, user.Email, user.DisplayName, user.IsActive, user.DepartmentId, user.CreatedAt, user.UpdatedAt, user.Version,
        (from membership in database.UserRoles join role in database.Roles on membership.RoleId equals role.Id
         where membership.UserId == user.Id orderby role.Name select new AccountRole(role.Id, role.Name)).ToList()));

    private IQueryable<AccountRoleDetails> ProjectRoles(IQueryable<Role> query) => query.Select(role => new AccountRoleDetails(
        role.Id, role.Name, role.Description, role.Version,
        (from grant in database.RolePermissions join permission in database.Permissions on grant.PermissionId equals permission.Id
         where grant.RoleId == role.Id orderby permission.Code select permission.Code).ToList()));

    private IQueryable<string> UserGrants(Guid id) =>
        from membership in database.UserRoles join grant in database.RolePermissions on membership.RoleId equals grant.RoleId
        join permission in database.Permissions on grant.PermissionId equals permission.Id
        where membership.UserId == id select permission.Code;

    private async Task<IDbContextTransaction> BeginAsync(AccountActor actor, string? permission, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Same lock as bootstrap/catalog: serialize privilege invariants across all API instances.
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
            await LockUserAsync(actor.UserId, cancellationToken);
            await RequireActorAsync(actor, permission, cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private async Task RequireActorAsync(AccountActor actor, string? permission, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw Error(AccountError.Unauthenticated);
        if (permission is not null && !await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, permission, cancellationToken))
            throw Error(AccountError.InsufficientScope);
    }

    private async Task<User> LockUserAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Users.FromSqlInterpolated($"SELECT *, xmin FROM identity.users WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw Error(AccountError.NotFound);

    private async Task CheckTargetScopeAsync(AccountActor actor, Guid targetId, CancellationToken cancellationToken)
    {
        var privileged = await IsAdminAsync(targetId, cancellationToken) ||
            await UserGrants(targetId).AnyAsync(x => x == PermissionCodes.UserManage || x == PermissionCodes.RoleManage, cancellationToken);
        if (privileged && (!await IsAdminAsync(actor.UserId, cancellationToken) ||
            !await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, PermissionCodes.RoleManage, cancellationToken)))
            throw Error(AccountError.InsufficientScope);
    }

    private Task<bool> IsAdminAsync(Guid id, CancellationToken cancellationToken) =>
        (from membership in database.UserRoles join role in database.Roles on membership.RoleId equals role.Id
         where membership.UserId == id && role.Name == RoleNames.SystemAdmin select membership.UserId).AnyAsync(cancellationToken);

    private async Task CheckOtherAdminAsync(Guid excludedId, CancellationToken cancellationToken)
    {
        if (!await (from membership in database.UserRoles join role in database.Roles on membership.RoleId equals role.Id
                    join user in database.Users on membership.UserId equals user.Id
                    where role.Name == RoleNames.SystemAdmin && user.IsActive && user.Id != excludedId
                    select user.Id).AnyAsync(cancellationToken)) throw Error(AccountError.LastActiveAdmin);
    }

    private Task<int> RevokeSessionsAsync(Guid id, CancellationToken cancellationToken) =>
        database.RefreshTokens.Where(x => x.UserId == id && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, Now), cancellationToken);

    private void Audit(AccountActor actor, string action, Guid? userId, Guid? roleId, object? data) =>
        database.IdentityAuditLogs.Add(new IdentityAuditLog { ActorId = actor.UserId, TargetUserId = userId,
            TargetRoleId = roleId, Action = action, Data = data is null ? null : JsonSerializer.Serialize(data), CreatedAt = Now });
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static AccountOperationException Error(AccountError error) => new(error);
    private static void CheckVersion(uint current, uint expected)
    {
        if (current != expected) throw Error(AccountError.VersionConflict);
    }
}
