using be.Application.Features.Accounts;
using be.Domain.Enums;

namespace be.Application.Features.Tickets;

public sealed record TicketWorkflowContext(uint Version, TicketStatus Status, bool CanAssign, bool AssignmentIsValid,
    IReadOnlyList<TicketStatus> Transitions, Guid? AssignedTeamId, string? AssignedTeamName,
    Guid? AssignedAgentId, string? AssignedAgentName);
public sealed record TicketAssignmentTeam(Guid Id, string Name, string DepartmentName);
public sealed record TicketAssignmentAgent(Guid Id, string DisplayName);
public sealed record TicketAssignmentSearch(int Page = 1, int PageSize = 20, string? Search = null);

public interface ITicketWorkflowReadStore
{
    Task<TicketWorkflowContext> ContextAsync(AccountActor actor, Guid id, CancellationToken cancellationToken);
    Task<PagedResult<TicketAssignmentTeam>> TeamsAsync(AccountActor actor, Guid id, TicketAssignmentSearch search, CancellationToken cancellationToken);
    Task<PagedResult<TicketAssignmentAgent>> AgentsAsync(AccountActor actor, Guid id, Guid teamId, TicketAssignmentSearch search, CancellationToken cancellationToken);
}

public sealed class TicketWorkflowReadService(ITicketWorkflowReadStore store)
{
    public Task<TicketWorkflowContext> ContextAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) =>
        store.ContextAsync(actor, id, cancellationToken);
    public Task<PagedResult<TicketAssignmentTeam>> TeamsAsync(AccountActor actor, Guid id, TicketAssignmentSearch search, CancellationToken cancellationToken) =>
        store.TeamsAsync(actor, id, Validate(search), cancellationToken);
    public Task<PagedResult<TicketAssignmentAgent>> AgentsAsync(AccountActor actor, Guid id, Guid teamId, TicketAssignmentSearch search, CancellationToken cancellationToken) =>
        store.AgentsAsync(actor, id, teamId, Validate(search), cancellationToken);
    private static TicketAssignmentSearch Validate(TicketAssignmentSearch search)
    {
        if (search.Page is < 1 or > 1_000_000 || search.PageSize is < 1 or > 100 || search.Search?.Length > 200)
            throw new TicketOperationException(TicketError.InvalidInput);
        return search with { Search = search.Search?.Trim() };
    }
}
