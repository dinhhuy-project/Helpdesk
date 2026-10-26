using be.Application.Features.Accounts;

namespace be.Application.Features.Departments;

public sealed class DepartmentService(IDepartmentStore store)
{
    public Task<PagedResult<DepartmentDetails>> ListAsync(DepartmentSearch search, CancellationToken cancellationToken) =>
        store.ListAsync(Normalize(search), cancellationToken);
    public Task<PagedResult<DepartmentLookup>> LookupAsync(DepartmentSearch search, CancellationToken cancellationToken) =>
        store.LookupAsync(Normalize(search) with { IsActive = true, SortBy = "name", Descending = false }, cancellationToken);
    public Task<DepartmentDetails> GetAsync(Guid id, CancellationToken cancellationToken) => store.GetAsync(id, cancellationToken);
    public Task<DepartmentDetails> CreateAsync(AccountActor actor, CreateDepartment command, CancellationToken cancellationToken) =>
        store.CreateAsync(actor, new(Name(command.Name), Description(command.Description)), cancellationToken);
    public Task<DepartmentDetails> UpdateAsync(AccountActor actor, Guid id, UpdateDepartment command, CancellationToken cancellationToken) =>
        store.UpdateAsync(actor, id, new(Name(command.Name), Description(command.Description), command.Version), cancellationToken);
    public Task<DepartmentDetails> SetStatusAsync(AccountActor actor, Guid id, DepartmentStatus command, CancellationToken cancellationToken) =>
        store.SetStatusAsync(actor, id, command, cancellationToken);

    private static DepartmentSearch Normalize(DepartmentSearch search)
    {
        if (search.Page is < 1 or > 1_000_000 || search.PageSize is < 1 or > 100 || search.Search?.Length > 200 ||
            search.SortBy is not ("name" or "createdAt")) throw new DepartmentOperationException(DepartmentError.InvalidInput);
        return search with { Search = search.Search?.Trim(' ') };
    }
    private static string Name(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200
        ? value.Trim(' ') : throw new DepartmentOperationException(DepartmentError.InvalidInput);
    private static string? Description(string? value) => value?.Length > 2000
        ? throw new DepartmentOperationException(DepartmentError.InvalidInput) : string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
