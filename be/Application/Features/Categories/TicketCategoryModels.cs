namespace be.Application.Features.Categories;

public sealed record TicketCategoryDetails(Guid Id, Guid? ParentId, string Name, string? Description,
    bool IsActive, bool IsAvailable, bool HasChildren, DateTime CreatedAt, DateTime? UpdatedAt, uint Version);
public sealed record TicketCategoryLookup(Guid Id, Guid? ParentId, string Name, bool HasChildren);
public sealed record CategorySearch(int Page = 1, int PageSize = 20, string? Search = null, Guid? ParentId = null,
    bool RootsOnly = false, bool? IsActive = null, string SortBy = "name", bool Descending = false);
public sealed record CreateTicketCategory(string Name, string? Description, Guid? ParentId);
public sealed record UpdateTicketCategory(string Name, string? Description, Guid? ParentId, uint Version);
public sealed record TicketCategoryStatus(bool IsActive, uint Version);
public enum CategoryError { NotFound, NameInUse, VersionConflict, InvalidInput, InvalidParent, CategoryCycle, Unauthenticated, InsufficientScope }
public sealed class CategoryOperationException(CategoryError error) : Exception("Ticket category operation failed.")
{
    public CategoryError Error { get; } = error;
}
