using System.ComponentModel.DataAnnotations;

namespace be.Contracts.Requests;

public sealed class TicketAssignmentSearchRequest : AccountPageRequest
{
    [StringLength(200)] public string? Search { get; init; }
}
