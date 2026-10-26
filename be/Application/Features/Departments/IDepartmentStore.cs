using be.Application.Features.Accounts;

namespace be.Application.Features.Departments;

public interface IDepartmentStore
{
    Task<PagedResult<DepartmentDetails>> ListAsync(DepartmentSearch search, CancellationToken cancellationToken);
    Task<PagedResult<DepartmentLookup>> LookupAsync(DepartmentSearch search, CancellationToken cancellationToken);
    Task<DepartmentDetails> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<DepartmentDetails> CreateAsync(AccountActor actor, CreateDepartment command, CancellationToken cancellationToken);
    Task<DepartmentDetails> UpdateAsync(AccountActor actor, Guid id, UpdateDepartment command, CancellationToken cancellationToken);
    Task<DepartmentDetails> SetStatusAsync(AccountActor actor, Guid id, DepartmentStatus command, CancellationToken cancellationToken);
}
