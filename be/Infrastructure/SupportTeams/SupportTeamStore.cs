using System.Text.Json;
using be.Application.Common.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.SupportTeams;
using be.Application.Features.Departments;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace be.Infrastructure.SupportTeams;

public sealed class SupportTeamStore(HelpdeskDbContext database, IPermissionChecker permissions,
    IAuthSessionStore sessions, TimeProvider clock) : ISupportTeamStore
{
    public async Task<TeamAdministrationAccess> AccessAsync(AccountActor actor, Guid? id, CancellationToken cancellationToken)
    {
        await RequireActorAsync(actor, cancellationToken);
        var team = id is null ? null : await GetAsync(actor, id.Value, cancellationToken);
        var admin = await IsAdminAsync(actor.UserId, cancellationToken);
        return new(admin, admin && team is not null && (team.IsActive || team.DepartmentIsActive), admin,
            team is not null && team.IsActive && team.DepartmentIsActive);
    }

    public async Task<PagedResult<DepartmentLookup>> DepartmentsAsync(AccountActor actor, TeamLookupSearch search, CancellationToken cancellationToken)
    {
        await RequireActorAsync(actor, cancellationToken);
        var admin = await IsAdminAsync(actor.UserId, cancellationToken);
        var visible = Visible(actor, admin);
        var query = database.Departments.AsNoTracking().Where(x => x.IsActive && (admin || visible.Any(t => t.DepartmentId == x.Id)));
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => x.NormalizedName.Contains(search.Search.ToLower()));
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.NormalizedName).ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(x => new DepartmentLookup(x.Id, x.Name)).ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<PagedResult<TeamMemberCandidate>> CandidatesAsync(AccountActor actor, Guid id, TeamLookupSearch search, CancellationToken cancellationToken)
    {
        var team = await GetAsync(actor, id, cancellationToken); // Scope before candidate names or count.
        if (!team.IsActive || !team.DepartmentIsActive) throw Error(TeamError.InactiveTeam);
        // An inactive existing membership must be reactivated, never re-created.
        var query = database.Users.AsNoTracking().Where(user => user.IsActive &&
            !database.SupportTeamMembers.Any(member => member.SupportTeamId == id && member.UserId == user.Id));
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => x.DisplayName.ToLower().Contains(search.Search.ToLower()));
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(x => new TeamMemberCandidate(x.Id, x.DisplayName,
                database.Departments.Where(d => d.Id == x.DepartmentId).Select(d => d.Name).FirstOrDefault())).ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<PagedResult<SupportTeamDetails>> ListAsync(AccountActor actor, TeamSearch search, CancellationToken cancellationToken)
    {
        await RequireActorAsync(actor, cancellationToken);
        var query = Visible(actor, await IsAdminAsync(actor.UserId, cancellationToken));
        if (search.DepartmentId is not null) query = query.Where(x => x.DepartmentId == search.DepartmentId);
        if (search.IsActive is not null) query = query.Where(x => x.IsActive == search.IsActive);
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => x.NormalizedName.Contains(search.Search.ToLower()));
        var count = await query.CountAsync(cancellationToken);
        var ordered = (search.SortBy, search.Descending) switch
        {
            ("createdAt", false) => query.OrderBy(x => x.CreatedAt),
            ("createdAt", true) => query.OrderByDescending(x => x.CreatedAt),
            (_, true) => query.OrderByDescending(x => x.NormalizedName),
            _ => query.OrderBy(x => x.NormalizedName)
        };
        var items = await Project(ordered.ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize))
            .ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<SupportTeamDetails> GetAsync(AccountActor actor, Guid id, CancellationToken cancellationToken)
    {
        await RequireActorAsync(actor, cancellationToken);
        return await Project(Visible(actor, await IsAdminAsync(actor.UserId, cancellationToken)).Where(x => x.Id == id))
            .SingleOrDefaultAsync(cancellationToken) ?? throw Error(TeamError.NotFound);
    }

    public async Task<PagedResult<TeamMemberDetails>> MembersAsync(AccountActor actor, Guid id, TeamMemberSearch search, CancellationToken cancellationToken)
    {
        await GetAsync(actor, id, cancellationToken); // Scope the parent before accessing any member data/count.
        var query = MemberQuery(id, isActive: search.IsActive);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<SupportTeamDetails> CreateAsync(AccountActor actor, CreateSupportTeam command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        await RequireAdminAsync(actor, cancellationToken);
        await RequireDepartmentAsync(command.DepartmentId, cancellationToken);
        var team = new SupportTeam { DepartmentId = command.DepartmentId, Name = command.Name,
            Description = command.Description, CreatedAt = Now };
        database.SupportTeams.Add(team);
        Audit(actor, "SupportTeamCreated", new { TeamId = team.Id, team.DepartmentId });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailsAsync(team.Id, cancellationToken);
    }

    public async Task<SupportTeamDetails> UpdateAsync(AccountActor actor, Guid id, UpdateSupportTeam command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var team = await LockTeamAsync(actor, id, command.Version, cancellationToken);
        if (team.Name != command.Name || team.Description != command.Description)
        {
            team.Name = command.Name;
            team.Description = command.Description;
            Bump(team);
            Audit(actor, "SupportTeamUpdated", new { TeamId = id, Fields = new[] { "name", "description" } });
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await DetailsAsync(id, cancellationToken);
    }

    public async Task<SupportTeamDetails> SetStatusAsync(AccountActor actor, Guid id, SupportTeamStatus command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        await RequireAdminAsync(actor, cancellationToken);
        var team = await LockTeamAsync(actor, id, command.Version, cancellationToken);
        if (command.IsActive) await RequireDepartmentAsync(team.DepartmentId, cancellationToken);
        if (team.IsActive != command.IsActive)
        {
            var previous = team.IsActive;
            team.IsActive = command.IsActive;
            Bump(team);
            Audit(actor, "SupportTeamStatusChanged", new { TeamId = id, Previous = previous, Current = command.IsActive });
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await DetailsAsync(id, cancellationToken);
    }

    public async Task<TeamMembershipResult> AddMemberAsync(AccountActor actor, Guid id, AddTeamMember command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var team = await LockTeamAsync(actor, id, command.TeamVersion, cancellationToken);
        await RequireAvailableAsync(team, cancellationToken);
        await RequireUserAsync(command.UserId, cancellationToken);
        if (await database.SupportTeamMembers.AnyAsync(x => x.SupportTeamId == id && x.UserId == command.UserId, cancellationToken))
            throw Error(TeamError.MembershipExists);
        database.SupportTeamMembers.Add(new SupportTeamMember { SupportTeamId = id, UserId = command.UserId, JoinedAt = Now });
        Bump(team);
        Audit(actor, "TeamMemberAdded", new { TeamId = id, command.UserId });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MembershipResultAsync(id, command.UserId, cancellationToken);
    }

    public async Task<TeamMembershipResult> SetMemberStatusAsync(AccountActor actor, Guid id, Guid userId, TeamMemberStatus command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var team = await LockTeamAsync(actor, id, command.TeamVersion, cancellationToken);
        var member = await LockMemberAsync(id, userId, command.Version, cancellationToken);
        // Delegated leaders cannot deactivate/reactivate themselves or other leaders.
        if (member.IsLeader) await RequireAdminAsync(actor, cancellationToken);
        if (command.IsActive)
        {
            await RequireAvailableAsync(team, cancellationToken);
            await RequireUserAsync(userId, cancellationToken);
        }
        if (member.IsActive != command.IsActive)
        {
            var previous = member.IsActive;
            member.IsActive = command.IsActive;
            member.UpdatedAt = Now;
            Bump(team);
            Audit(actor, "TeamMemberStatusChanged", new { TeamId = id, UserId = userId, Previous = previous, Current = command.IsActive });
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await MembershipResultAsync(id, userId, cancellationToken);
    }

    public async Task<TeamMembershipResult> SetLeadershipAsync(AccountActor actor, Guid id, Guid userId, TeamMemberLeadership command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        await RequireAdminAsync(actor, cancellationToken);
        var team = await LockTeamAsync(actor, id, command.TeamVersion, cancellationToken);
        var member = await LockMemberAsync(id, userId, command.Version, cancellationToken);
        if (command.IsLeader)
        {
            await RequireAvailableAsync(team, cancellationToken);
            await RequireUserAsync(userId, cancellationToken);
            if (!member.IsActive || !await HasGrantAsync(userId, PermissionCodes.TeamManage, cancellationToken))
                throw Error(TeamError.InvalidLeader);
        }
        if (member.IsLeader != command.IsLeader)
        {
            var previous = member.IsLeader;
            member.IsLeader = command.IsLeader;
            member.UpdatedAt = Now;
            Bump(team);
            Audit(actor, "TeamLeadershipChanged", new { TeamId = id, UserId = userId, Previous = previous, Current = command.IsLeader });
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await MembershipResultAsync(id, userId, cancellationToken);
    }

    private IQueryable<SupportTeam> Visible(AccountActor actor, bool isAdmin)
    {
        var query = database.SupportTeams.AsNoTracking();
        return isAdmin ? query : query.Where(team => team.IsActive &&
            database.Departments.Any(d => d.Id == team.DepartmentId && d.IsActive) &&
            database.SupportTeamMembers.Any(m => m.SupportTeamId == team.Id && m.UserId == actor.UserId && m.IsActive && m.IsLeader));
    }
    private IQueryable<SupportTeamDetails> Project(IQueryable<SupportTeam> query) => query.Select(x =>
        new SupportTeamDetails(x.Id, x.DepartmentId, x.Name, x.Description, x.IsActive,
            database.Departments.Any(d => d.Id == x.DepartmentId && d.IsActive), x.CreatedAt, x.UpdatedAt, x.Version,
            database.Departments.Where(d => d.Id == x.DepartmentId).Select(d => d.Name).Single()));
    private IQueryable<TeamMemberDetails> MemberQuery(Guid id, Guid? userId = null, bool? isActive = null)
    {
        var query = from member in database.SupportTeamMembers.AsNoTracking()
                    join user in database.Users.AsNoTracking() on member.UserId equals user.Id
                    where member.SupportTeamId == id
                    select new { Member = member, User = user };
        if (userId is not null) query = query.Where(x => x.Member.UserId == userId);
        if (isActive is not null) query = query.Where(x => x.Member.IsActive == isActive);
        // Filter/sort columns before constructing a positional DTO so EF can translate them.
        return query.OrderBy(x => x.User.DisplayName).ThenBy(x => x.User.Id).Select(x =>
            new TeamMemberDetails(x.User.Id, x.User.DisplayName, x.User.IsActive, x.Member.IsActive,
                x.Member.IsLeader, x.Member.JoinedAt, x.Member.UpdatedAt, x.Member.Version,
                x.Member.IsActive && x.User.IsActive && database.SupportTeams.Any(team => team.Id == id && team.IsActive &&
                    database.Departments.Any(d => d.Id == team.DepartmentId && d.IsActive)) &&
                (from membership in database.UserRoles join grant in database.RolePermissions on membership.RoleId equals grant.RoleId
                 join permission in database.Permissions on grant.PermissionId equals permission.Id
                 where membership.UserId == x.User.Id && permission.Code == PermissionCodes.TeamManage select permission.Id).Any()));
    }
    private Task<SupportTeamDetails> DetailsAsync(Guid id, CancellationToken cancellationToken) =>
        Project(database.SupportTeams.AsNoTracking().Where(x => x.Id == id)).SingleAsync(cancellationToken);
    private async Task<TeamMembershipResult> MembershipResultAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        new(await DetailsAsync(id, cancellationToken), await MemberQuery(id, userId).SingleAsync(cancellationToken));

    private async Task<IDbContextTransaction> BeginAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Account/department/Identity administration use this same lock and actor-first order.
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
            var user = await database.Users.FromSqlInterpolated($"SELECT *, xmin FROM identity.users WHERE id = {actor.UserId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (user is null) throw Error(TeamError.Unauthenticated);
            await RequireActorAsync(actor, cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
    private async Task RequireActorAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw Error(TeamError.Unauthenticated);
        if (!await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, PermissionCodes.TeamManage, cancellationToken))
            throw Error(TeamError.InsufficientScope);
    }
    private async Task<SupportTeam> LockTeamAsync(AccountActor actor, Guid id, uint version, CancellationToken cancellationToken)
    {
        if (!await Visible(actor, await IsAdminAsync(actor.UserId, cancellationToken)).AnyAsync(x => x.Id == id, cancellationToken))
            throw Error(TeamError.NotFound);
        var team = await database.SupportTeams.FromSqlInterpolated($"SELECT *, xmin FROM organization.support_teams WHERE id = {id} FOR UPDATE")
            .SingleAsync(cancellationToken);
        CheckVersion(team.Version, version);
        return team;
    }
    private async Task<SupportTeamMember> LockMemberAsync(Guid id, Guid userId, uint version, CancellationToken cancellationToken)
    {
        var member = await database.SupportTeamMembers.FromSqlInterpolated(
            $"SELECT *, xmin FROM organization.support_team_members WHERE support_team_id = {id} AND user_id = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw Error(TeamError.NotFound);
        CheckVersion(member.Version, version);
        return member;
    }
    private async Task RequireAdminAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(actor.UserId, cancellationToken)) throw Error(TeamError.InsufficientScope);
    }
    private Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        (from membership in database.UserRoles join role in database.Roles on membership.RoleId equals role.Id
         where membership.UserId == userId && role.Name == RoleNames.SystemAdmin select membership.UserId).AnyAsync(cancellationToken);
    private Task<bool> HasGrantAsync(Guid userId, string code, CancellationToken cancellationToken) =>
        (from membership in database.UserRoles join grant in database.RolePermissions on membership.RoleId equals grant.RoleId
         join permission in database.Permissions on grant.PermissionId equals permission.Id
         where membership.UserId == userId && permission.Code == code select membership.UserId).AnyAsync(cancellationToken);
    private async Task RequireDepartmentAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await database.Departments.AnyAsync(x => x.Id == id && x.IsActive, cancellationToken)) throw Error(TeamError.InvalidDepartment);
    }
    private async Task RequireUserAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await database.Users.AnyAsync(x => x.Id == id && x.IsActive, cancellationToken)) throw Error(TeamError.InvalidUser);
    }
    private async Task RequireAvailableAsync(SupportTeam team, CancellationToken cancellationToken)
    {
        if (!team.IsActive) throw Error(TeamError.InactiveTeam);
        await RequireDepartmentAsync(team.DepartmentId, cancellationToken);
    }
    private void Bump(SupportTeam team)
    {
        team.UpdatedAt = Now;
        database.Entry(team).Property(x => x.UpdatedAt).IsModified = true;
    }
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_support_teams_department_id_normalized_name" })
        { throw Error(TeamError.NameInUse); }
    }
    private void Audit(AccountActor actor, string action, object data) => database.IdentityAuditLogs.Add(
        new IdentityAuditLog { ActorId = actor.UserId, Action = action, Data = JsonSerializer.Serialize(data), CreatedAt = Now });
    private static void CheckVersion(uint current, uint expected) { if (current != expected) throw Error(TeamError.VersionConflict); }
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static TeamOperationException Error(TeamError error) => new(error);
}
