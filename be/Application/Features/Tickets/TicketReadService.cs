using be.Application.Features.Accounts;

namespace be.Application.Features.Tickets;

public sealed class TicketReadService(ITicketReadStore store)
{
    public Task<PagedResult<TicketSummary>> ListAsync(AccountActor actor, TicketSearch search, CancellationToken cancellationToken)
    {
        if (search.Page is < 1 or > 1_000_000 || search.PageSize is < 1 or > 100 || search.Search?.Length > 200 ||
            search.TeamId == Guid.Empty || search.CategoryId == Guid.Empty || search.RequesterId == Guid.Empty || search.AgentId == Guid.Empty ||
            search.TicketNumber <= 0 || search.SortBy is not ("createdAt" or "updatedAt" or "ticketNumber" or "priority") ||
            (search.CreatedFrom is not null && search.CreatedTo is not null && search.CreatedFrom >= search.CreatedTo) ||
            (search.Status is not null && !Enum.IsDefined(search.Status.Value)) ||
            (search.Priority is not null && !Enum.IsDefined(search.Priority.Value)))
            throw new TicketOperationException(TicketError.InvalidInput);
        return store.ListAsync(actor, search with { Search = search.Search?.Trim(),
            CreatedFrom = search.CreatedFrom?.ToUniversalTime(), CreatedTo = search.CreatedTo?.ToUniversalTime() }, cancellationToken);
    }
    public Task<TicketDetails> GetAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) => store.GetAsync(actor, id, cancellationToken);
    public Task<TicketAccess> AccessAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) => store.AccessAsync(actor, id, cancellationToken);
}
