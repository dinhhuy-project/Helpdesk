using be.Domain.Enums;

namespace be.Application.Features.Tickets;

public enum TicketOperation { Read, UpdateStatus, Assign }
public sealed record TicketAccess(bool CanRead, bool CanUpdateStatus, bool CanAssign);
public sealed record TicketAssignmentTarget(Guid TeamId, Guid? AgentId);
public sealed record TicketSearch(int Page = 1, int PageSize = 20, string? Search = null,
    TicketStatus? Status = null, TicketPriority? Priority = null, Guid? TeamId = null, Guid? CategoryId = null);
public sealed record TicketSummary(Guid Id, long TicketNumber, string Title, TicketStatus Status, TicketPriority Priority,
    Guid RequesterId, Guid CategoryId, Guid? AssignedTeamId, Guid? AssignedAgentId, DateTime CreatedAt, DateTime? UpdatedAt, uint Version);
public sealed record TicketDetails(Guid Id, long TicketNumber, string Title, string Description, TicketStatus Status, TicketPriority Priority,
    Guid RequesterId, Guid CategoryId, Guid? AssignedTeamId, Guid? AssignedAgentId, string? ResolutionSummary,
    DateTime CreatedAt, DateTime? UpdatedAt, DateTime? ResolvedAt, DateTime? ClosedAt, uint Version);
public enum TicketError { NotFound, Unauthenticated, Forbidden, InvalidInput, InvalidAssignment, VersionConflict }
public sealed class TicketOperationException(TicketError error) : Exception("Ticket operation failed.")
{
    public TicketError Error { get; } = error;
}
