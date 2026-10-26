using be.Application.Features.Accounts;

namespace be.Application.Features.Categories;

public interface ITicketCategoryStore
{
    Task<PagedResult<TicketCategoryDetails>> ListAsync(CategorySearch search, CancellationToken cancellationToken);
    Task<PagedResult<TicketCategoryLookup>> LookupAsync(CategorySearch search, CancellationToken cancellationToken);
    Task<TicketCategoryDetails> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<TicketCategoryDetails> CreateAsync(AccountActor actor, CreateTicketCategory command, CancellationToken cancellationToken);
    Task<TicketCategoryDetails> UpdateAsync(AccountActor actor, Guid id, UpdateTicketCategory command, CancellationToken cancellationToken);
    Task<TicketCategoryDetails> SetStatusAsync(AccountActor actor, Guid id, TicketCategoryStatus command, CancellationToken cancellationToken);
}
