using be.Application.Features.Accounts;

namespace be.Application.Features.Categories;

public sealed class TicketCategoryService(ITicketCategoryStore store)
{
    public Task<PagedResult<TicketCategoryDetails>> ListAsync(CategorySearch search, CancellationToken cancellationToken) =>
        store.ListAsync(Normalize(search), cancellationToken);
    public Task<PagedResult<TicketCategoryLookup>> LookupAsync(CategorySearch search, CancellationToken cancellationToken) =>
        store.LookupAsync(Normalize(search) with { IsActive = true, SortBy = "name", Descending = false }, cancellationToken);
    public Task<TicketCategoryDetails> GetAsync(Guid id, CancellationToken cancellationToken) => store.GetAsync(id, cancellationToken);
    public Task<TicketCategoryDetails> CreateAsync(AccountActor actor, CreateTicketCategory command, CancellationToken cancellationToken)
    {
        Parent(command.ParentId);
        return store.CreateAsync(actor, command with { Name = Name(command.Name), Description = Description(command.Description) }, cancellationToken);
    }
    public Task<TicketCategoryDetails> UpdateAsync(AccountActor actor, Guid id, UpdateTicketCategory command, CancellationToken cancellationToken)
    {
        Parent(command.ParentId);
        if (command.Version == 0) Invalid();
        return store.UpdateAsync(actor, id, command with { Name = Name(command.Name), Description = Description(command.Description) }, cancellationToken);
    }
    public Task<TicketCategoryDetails> SetStatusAsync(AccountActor actor, Guid id, TicketCategoryStatus command, CancellationToken cancellationToken)
    {
        if (command.Version == 0) Invalid();
        return store.SetStatusAsync(actor, id, command, cancellationToken);
    }
    private static CategorySearch Normalize(CategorySearch search)
    {
        if (search.Page is < 1 or > 1_000_000 || search.PageSize is < 1 or > 100 || search.Search?.Length > 200 ||
            search.SortBy is not ("name" or "createdAt") || (search.RootsOnly && search.ParentId is not null)) Invalid();
        Parent(search.ParentId);
        return search with { Search = search.Search?.Trim(' ') };
    }
    private static void Parent(Guid? id) { if (id == Guid.Empty) Invalid(); }
    private static string Name(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200
        ? value.Trim(' ') : throw new CategoryOperationException(CategoryError.InvalidInput);
    private static string? Description(string? value) => value?.Length > 2000
        ? throw new CategoryOperationException(CategoryError.InvalidInput) : string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Invalid() => throw new CategoryOperationException(CategoryError.InvalidInput);
}
