using System.Data;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Tickets;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Domain.Rules;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Tickets;

public sealed class TicketWorkflowReadStore(HelpdeskDbContext database, TicketScope scope, IAuthSessionStore sessions) : ITicketWorkflowReadStore
{
    public async Task<TicketWorkflowContext> ContextAsync(AccountActor actor, Guid id, CancellationToken cancellationToken)
    {
        // One snapshot for version, assignment and capabilities. Commands always recheck under their write lock.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var ticket = await RequireTicketAsync(actor, id, false, cancellationToken);
        var valid = ticket.AssignedTeamId is not null && ticket.AssignedAgentId is not null &&
            await scope.IsAssignmentActiveAsync(new(ticket.AssignedTeamId.Value, ticket.AssignedAgentId), cancellationToken);
        var transitions = new List<TicketStatus>();
        foreach (var status in Enum.GetValues<TicketStatus>().Where(target => TicketWorkflow.CanTransition(ticket.Status, target)))
        {
            var operation = TicketTransitionOperation.For(status);
            if ((operation != TicketOperation.UpdateStatus || valid) &&
                await scope.For(actor, operation).AnyAsync(x => x.Id == id, cancellationToken)) transitions.Add(status);
        }
        var teamName = await database.SupportTeams.Where(x => x.Id == ticket.AssignedTeamId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken);
        var agentName = await database.Users.Where(x => x.Id == ticket.AssignedAgentId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        var canAssign = await scope.For(actor, TicketOperation.Assign).AnyAsync(x => x.Id == id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(ticket.Version, ticket.Status, canAssign, valid, transitions,
            ticket.AssignedTeamId, teamName, ticket.AssignedAgentId, agentName);
    }

    public async Task<PagedResult<TicketAssignmentTeam>> TeamsAsync(AccountActor actor, Guid id, TicketAssignmentSearch search, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await RequireTicketAsync(actor, id, true, cancellationToken);
        var query = from team in scope.AssignmentTeams(actor)
                    join department in database.Departments on team.DepartmentId equals department.Id
                    select new { team.Id, team.Name, DepartmentName = department.Name };
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => x.Name.ToLower().Contains(search.Search.ToLower()) || x.DepartmentName.ToLower().Contains(search.Search.ToLower()));
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(x => new TicketAssignmentTeam(x.Id, x.Name, x.DepartmentName)).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<PagedResult<TicketAssignmentAgent>> AgentsAsync(AccountActor actor, Guid id, Guid teamId, TicketAssignmentSearch search, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        // Source ACL comes first, even if the caller guesses a valid destination.
        await RequireTicketAsync(actor, id, true, cancellationToken);
        if (!await scope.AssignmentTeams(actor).AnyAsync(x => x.Id == teamId, cancellationToken)) throw Error(TicketError.NotFound);
        var query = scope.AssignmentAgents(teamId);
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => x.DisplayName.ToLower().Contains(search.Search.ToLower()));
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(x => new TicketAssignmentAgent(x.Id, x.DisplayName)).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    private async Task<Ticket> RequireTicketAsync(AccountActor actor, Guid id, bool assignment, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw Error(TicketError.Unauthenticated);
        if (!await scope.HasReadGrantAsync(actor, cancellationToken)) throw Error(TicketError.Forbidden);
        var ticket = await scope.For(actor, TicketOperation.Read).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw Error(TicketError.NotFound);
        if (assignment && !await scope.For(actor, TicketOperation.Assign).AnyAsync(x => x.Id == id, cancellationToken)) throw Error(TicketError.Forbidden);
        return ticket;
    }
    private static TicketOperationException Error(TicketError error) => new(error);
}
