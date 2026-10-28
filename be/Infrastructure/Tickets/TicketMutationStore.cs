using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Entities;
using be.Domain.Constants;
using be.Domain.Enums;
using be.Domain.Rules;
using be.Infrastructure.Persistence;
using be.Infrastructure.Notifications;
using be.Infrastructure.Sla;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Tickets;

public sealed class TicketMutationStore(HelpdeskDbContext database, TicketWriteGuard guard, TicketScope scope, TimeProvider clock,
    TicketNotificationWriter notifications, TicketSlaTracker slaTracker) : ITicketMutationStore
{
    public async Task<TicketMutationResult> AssignAsync(AccountActor actor, Guid id, AssignTicket command, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var ticket = await guard.LockAssignmentAsync(actor, id, command.Version, new(command.TeamId, command.AgentId), cancellationToken);
        if (ticket.AssignedTeamId != command.TeamId || ticket.AssignedAgentId != command.AgentId)
        {
            var oldValue = JsonSerializer.Serialize(new { ticket.AssignedTeamId, ticket.AssignedAgentId });
            ticket.AssignedTeamId = command.TeamId;
            ticket.AssignedAgentId = command.AgentId;
            ticket.UpdatedAt = UtcNow();
            database.TicketActivities.Add(new TicketActivity { TicketId = id, ActorId = actor.UserId, Action = TicketActivityActions.Assigned,
                CreatedAt = ticket.UpdatedAt.Value, OldValue = oldValue,
                NewValue = JsonSerializer.Serialize(new { ticket.AssignedTeamId, ticket.AssignedAgentId }) });
            await notifications.SaveChangesAsync(actor.UserId, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        // The actor may lose team read scope after transfer, so return the command receipt directly.
        return Receipt(ticket);
    }

    public async Task<TicketMutationResult> TransitionAsync(AccountActor actor, Guid id, TransitionTicket command, CancellationToken cancellationToken)
    {
        var operation = command.Status switch
        {
            TicketStatus.Closed => TicketOperation.Confirm,
            TicketStatus.Reopened => TicketOperation.Reopen,
            TicketStatus.Cancelled => TicketOperation.Cancel,
            _ => TicketOperation.UpdateStatus
        };
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var ticket = await guard.LockAsync(actor, id, operation, command.Version, cancellationToken);
        var oldValue = State(ticket);
        var now = UtcNow();
        try { TicketWorkflow.Apply(ticket, command.Status, command.ResolutionSummary, now); }
        catch (TicketWorkflowException error)
        {
            throw new TicketOperationException(error.Error switch
            {
                TicketWorkflowError.ResolutionRequired => TicketError.ResolutionRequired,
                TicketWorkflowError.InvalidInput => TicketError.InvalidInput,
                _ => TicketError.InvalidTransition
            });
        }
        if (operation == TicketOperation.UpdateStatus &&
            (ticket.AssignedTeamId is null || ticket.AssignedAgentId is null ||
             !await scope.IsAssignmentActiveAsync(new(ticket.AssignedTeamId.Value, ticket.AssignedAgentId), cancellationToken)))
            throw new TicketOperationException(TicketError.InvalidAssignment);

        await slaTracker.TransitionAsync(ticket, now, actor.UserId, cancellationToken);
        database.TicketActivities.Add(new TicketActivity { TicketId = id, ActorId = actor.UserId, Action = TicketActivityActions.StatusChanged,
            CreatedAt = now, OldValue = oldValue, NewValue = State(ticket) });
        await notifications.SaveChangesAsync(actor.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Receipt(ticket);
    }

    private static string State(Ticket ticket) => JsonSerializer.Serialize(new
    {
        Status = ticket.Status.ToString(), ticket.ResolutionSummary, ticket.ResolvedAt, ticket.ClosedAt
    });
    private static TicketMutationResult Receipt(Ticket ticket) => new(ticket.Id, ticket.Status, ticket.AssignedTeamId,
        ticket.AssignedAgentId, ticket.UpdatedAt, ticket.ResolvedAt, ticket.ClosedAt, ticket.Version);
    private DateTime UtcNow()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // PostgreSQL timestamptz has microsecond precision; receipts match subsequent reads.
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }
}
