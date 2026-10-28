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
    // One predicate for offline recipients and live request ACL. A reader is an active
    // account with current grants/membership; requests additionally require a live session.
    public IQueryable<TicketReader> Readers()
    {
        var grants = from membership in database.UserRoles
                     join grant in database.RolePermissions on membership.RoleId equals grant.RoleId
                     join permission in database.Permissions on grant.PermissionId equals permission.Id
                     select new { membership.UserId, permission.Code };
        var members = from member in database.SupportTeamMembers
                      join team in database.SupportTeams on member.SupportTeamId equals team.Id
                      join department in database.Departments on team.DepartmentId equals department.Id
                      where member.IsActive && team.IsActive && department.IsActive select member;
        return from ticket in database.Tickets
               from user in database.Users
               where user.IsActive &&
                   (grants.Any(g => g.UserId == user.Id && g.Code == PermissionCodes.TicketReadAll) ||
                    (ticket.RequesterId == user.Id && grants.Any(g => g.UserId == user.Id && g.Code == PermissionCodes.TicketReadOwn)) ||
                    (grants.Any(g => g.UserId == user.Id && g.Code == PermissionCodes.TicketReadTeam) &&
                     members.Any(m => m.UserId == user.Id && m.SupportTeamId == ticket.AssignedTeamId)))
               select new TicketReader { TicketId = ticket.Id, UserId = user.Id, CanReadInternal = ticket.RequesterId != user.Id &&
                   grants.Any(g => g.UserId == user.Id && g.Code == PermissionCodes.TicketCommentInternal) &&
                   (grants.Any(g => g.UserId == user.Id && g.Code == PermissionCodes.TicketReadAll) ||
                    members.Any(m => m.UserId == user.Id && m.SupportTeamId == ticket.AssignedTeamId)) };
    }

    public IQueryable<Ticket> For(AccountActor actor, TicketOperation operation)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var grants = Grants(actor.UserId);
        var members = ActiveMembers(actor.UserId);
        var readers = Readers().Where(reader => reader.UserId == actor.UserId);
        var query = database.Tickets.Where(ticket =>
            actor.UserId != Guid.Empty && actor.SessionId != Guid.Empty &&
            database.RefreshTokens.Any(session => session.Id == actor.SessionId && session.UserId == actor.UserId &&
                session.RevokedAt == null && session.ExpiresAt > now &&
                database.Users.Any(user => user.Id == actor.UserId && user.IsActive)) &&
            readers.Any(reader => reader.TicketId == ticket.Id));
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
            TicketOperation.ReadInternal => query.Where(ticket => readers.Any(reader => reader.TicketId == ticket.Id && reader.CanReadInternal)),
            TicketOperation.CommentPublic => query.Where(ticket => ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled &&
                grants.Contains(PermissionCodes.TicketCommentPublic)),
            TicketOperation.CommentInternal => query.Where(ticket => ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled &&
                readers.Any(reader => reader.TicketId == ticket.Id && reader.CanReadInternal)),
            _ => query.Where(_ => false)
        };
    }

    public Task<bool> HasReadGrantAsync(AccountActor actor, CancellationToken cancellationToken) => Grants(actor.UserId)
        .AnyAsync(code => code == PermissionCodes.TicketReadOwn || code == PermissionCodes.TicketReadTeam || code == PermissionCodes.TicketReadAll,
            cancellationToken);

    // Compare at report boundaries without exposing permission or membership data to clients.
    public async Task<string> ReadAccessSignatureAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var grants = await Grants(actor.UserId).Where(code => code == PermissionCodes.TicketReadAll ||
            code == PermissionCodes.TicketReadOwn || code == PermissionCodes.TicketReadTeam).Distinct().ToArrayAsync(cancellationToken);
        if (grants.Contains(PermissionCodes.TicketReadAll)) return "all";
        var teams = grants.Contains(PermissionCodes.TicketReadTeam)
            ? await ActiveMembers(actor.UserId).Select(x => x.SupportTeamId).Distinct().OrderBy(x => x).ToArrayAsync(cancellationToken) : [];
        return (grants.Contains(PermissionCodes.TicketReadOwn) ? "own:" : "team:") + string.Join(',', teams.Select(x => x.ToString("N")));
    }

    public IQueryable<TicketComment> CommentsFor(AccountActor actor)
    {
        var readable = For(actor, TicketOperation.Read).Select(ticket => ticket.Id);
        var internalReadable = For(actor, TicketOperation.ReadInternal).Select(ticket => ticket.Id);
        return database.TicketComments.AsNoTracking().Where(comment => readable.Contains(comment.TicketId) &&
            (!comment.IsInternal || internalReadable.Contains(comment.TicketId)));
    }

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

public sealed class TicketReader
{
    public Guid TicketId { get; init; }
    public Guid UserId { get; init; }
    public bool CanReadInternal { get; init; }
}
