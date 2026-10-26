using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Tickets;

// IQueryable stays within Infrastructure. Every consumer uses the same database predicate.
public sealed class TicketScope(HelpdeskDbContext database, TimeProvider clock)
{
    public IQueryable<Ticket> For(AccountActor actor, TicketOperation operation)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var grants = Grants(actor.UserId);
        var members = ActiveMembers(actor.UserId);
        var query = database.Tickets.Where(ticket =>
            actor.UserId != Guid.Empty && actor.SessionId != Guid.Empty &&
            database.RefreshTokens.Any(session => session.Id == actor.SessionId && session.UserId == actor.UserId &&
                session.RevokedAt == null && session.ExpiresAt > now &&
                database.Users.Any(user => user.Id == actor.UserId && user.IsActive)) &&
            (grants.Contains(PermissionCodes.TicketReadAll) ||
             (grants.Contains(PermissionCodes.TicketReadOwn) && ticket.RequesterId == actor.UserId) ||
             (grants.Contains(PermissionCodes.TicketReadTeam) && members.Any(m => m.SupportTeamId == ticket.AssignedTeamId))));
        return operation switch
        {
            TicketOperation.Read => query,
            TicketOperation.UpdateStatus => query.Where(ticket => ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled &&
                grants.Contains(PermissionCodes.TicketUpdateStatus) &&
                (grants.Contains(PermissionCodes.TicketReadAll) ||
                 members.Any(m => m.SupportTeamId == ticket.AssignedTeamId && (m.IsLeader || ticket.AssignedAgentId == actor.UserId)))),
            TicketOperation.Assign => query.Where(ticket => ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled &&
                grants.Contains(PermissionCodes.TicketAssign) &&
                (grants.Contains(PermissionCodes.TicketReadAll) || members.Any(m => m.SupportTeamId == ticket.AssignedTeamId && m.IsLeader))),
            TicketOperation.Confirm => RequesterScope(query, grants, actor.UserId, PermissionCodes.TicketConfirm),
            TicketOperation.Reopen => RequesterScope(query, grants, actor.UserId, PermissionCodes.TicketReopen),
            TicketOperation.Cancel => RequesterScope(query, grants, actor.UserId, PermissionCodes.TicketCancel),
            _ => query.Where(_ => false)
        };
    }

    public Task<bool> HasReadGrantAsync(AccountActor actor, CancellationToken cancellationToken) => Grants(actor.UserId)
        .AnyAsync(code => code == PermissionCodes.TicketReadOwn || code == PermissionCodes.TicketReadTeam || code == PermissionCodes.TicketReadAll,
            cancellationToken);

    public async Task<bool> CanAssignToAsync(AccountActor actor, Guid id, TicketAssignmentTarget target, CancellationToken cancellationToken)
    {
        if (target.TeamId == Guid.Empty || target.AgentId == Guid.Empty ||
            !await For(actor, TicketOperation.Assign).AnyAsync(x => x.Id == id, cancellationToken)) return false;
        var grants = Grants(actor.UserId);
        var leaders = ActiveMembers(actor.UserId).Where(m => m.IsLeader);
        // Validate destination independently of current Ticket scope: client IDs cannot widen access.
        return await database.SupportTeams.AnyAsync(team => team.Id == target.TeamId && team.IsActive &&
            database.Departments.Any(d => d.Id == team.DepartmentId && d.IsActive) &&
            (grants.Contains(PermissionCodes.TicketReadAll) || leaders.Any(m => m.SupportTeamId == team.Id)), cancellationToken) &&
            await IsAssignmentActiveAsync(target, cancellationToken);
    }

    public Task<bool> IsAssignmentActiveAsync(TicketAssignmentTarget target, CancellationToken cancellationToken)
    {
        var processors = from membership in database.UserRoles
                         join grant in database.RolePermissions on membership.RoleId equals grant.RoleId
                         join permission in database.Permissions on grant.PermissionId equals permission.Id
                         where permission.Code == PermissionCodes.TicketUpdateStatus select membership.UserId;
        var readers = from membership in database.UserRoles
                      join grant in database.RolePermissions on membership.RoleId equals grant.RoleId
                      join permission in database.Permissions on grant.PermissionId equals permission.Id
                      where permission.Code == PermissionCodes.TicketReadTeam || permission.Code == PermissionCodes.TicketReadAll
                      select membership.UserId;
        var agents = database.Users.Where(user => user.IsActive && processors.Contains(user.Id) && readers.Contains(user.Id) &&
            database.SupportTeamMembers.Any(m => m.SupportTeamId == target.TeamId && m.UserId == user.Id && m.IsActive));
        return database.SupportTeams.AnyAsync(team => team.Id == target.TeamId && team.IsActive &&
            database.Departments.Any(d => d.Id == team.DepartmentId && d.IsActive) &&
            (target.AgentId == null || agents.Any(user => user.Id == target.AgentId)), cancellationToken);
    }

    private static IQueryable<Ticket> RequesterScope(IQueryable<Ticket> query, IQueryable<string> grants, Guid userId, string permission) =>
        query.Where(ticket => ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled &&
            grants.Contains(permission) && (ticket.RequesterId == userId || grants.Contains(PermissionCodes.TicketReadAll)));

    private IQueryable<string> Grants(Guid userId) =>
        from membership in database.UserRoles join grant in database.RolePermissions on membership.RoleId equals grant.RoleId
        join permission in database.Permissions on grant.PermissionId equals permission.Id
        where membership.UserId == userId select permission.Code;

    private IQueryable<SupportTeamMember> ActiveMembers(Guid userId) => database.SupportTeamMembers.Where(member =>
        member.UserId == userId && member.IsActive && database.SupportTeams.Any(team => team.Id == member.SupportTeamId && team.IsActive &&
            database.Departments.Any(department => department.Id == team.DepartmentId && department.IsActive)));
}
