using be.Application.Features.Accounts;
using be.Domain.Enums;
using be.Domain.Rules;

namespace be.Application.Features.Tickets;

public sealed record AssignTicket(Guid TeamId, Guid? AgentId, uint Version);
public sealed record TransitionTicket(TicketStatus Status, string? ResolutionSummary, uint Version);
public sealed record TicketMutationResult(Guid Id, TicketStatus Status, Guid? AssignedTeamId, Guid? AssignedAgentId,
    DateTime? UpdatedAt, DateTime? ResolvedAt, DateTime? ClosedAt, uint Version);

public interface ITicketMutationStore
{
    Task<TicketMutationResult> AssignAsync(AccountActor actor, Guid id, AssignTicket command, CancellationToken cancellationToken);
    Task<TicketMutationResult> TransitionAsync(AccountActor actor, Guid id, TransitionTicket command, CancellationToken cancellationToken);
}

public sealed class TicketMutationService(ITicketMutationStore store)
{
    public Task<TicketMutationResult> AssignAsync(AccountActor actor, Guid id, AssignTicket command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || command.TeamId == Guid.Empty || command.AgentId == Guid.Empty || command.Version == 0)
            throw new TicketOperationException(TicketError.InvalidInput);
        return store.AssignAsync(actor, id, command, cancellationToken);
    }

    public Task<TicketMutationResult> TransitionAsync(AccountActor actor, Guid id, TransitionTicket command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || command.Version == 0 || !Enum.IsDefined(command.Status) ||
            command.ResolutionSummary?.Length > TicketWorkflow.ResolutionSummaryLimit ||
            (command.Status != TicketStatus.Resolved && command.ResolutionSummary is not null))
            throw new TicketOperationException(TicketError.InvalidInput);
        return store.TransitionAsync(actor, id, command, cancellationToken);
    }
}
