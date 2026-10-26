using be.Application.Features.Accounts;

namespace be.Application.Features.Tickets;

public interface ITicketReadStore
{
    Task<PagedResult<TicketSummary>> ListAsync(AccountActor actor, TicketSearch search, CancellationToken cancellationToken);
    Task<TicketDetails> GetAsync(AccountActor actor, Guid id, CancellationToken cancellationToken);
    Task<TicketAccess> AccessAsync(AccountActor actor, Guid id, CancellationToken cancellationToken);
}
