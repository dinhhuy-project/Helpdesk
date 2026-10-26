namespace be.Application.Features.Departments;

public sealed record DepartmentDetails(Guid Id, string Name, string? Description, bool IsActive,
    DateTime CreatedAt, DateTime? UpdatedAt, uint Version);
public sealed record DepartmentLookup(Guid Id, string Name);
public sealed record DepartmentSearch(int Page = 1, int PageSize = 20, string? Search = null,
    bool? IsActive = null, string SortBy = "name", bool Descending = false);
public sealed record CreateDepartment(string Name, string? Description);
public sealed record UpdateDepartment(string Name, string? Description, uint Version);
public sealed record DepartmentStatus(bool IsActive, uint Version);

public enum DepartmentError { NotFound, NameInUse, VersionConflict, InvalidInput, Unauthenticated, InsufficientScope }
public sealed class DepartmentOperationException(DepartmentError error) : Exception("Department operation failed.")
{
    public DepartmentError Error { get; } = error;
}
