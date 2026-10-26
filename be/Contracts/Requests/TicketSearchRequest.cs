using System.ComponentModel.DataAnnotations;
using be.Domain.Enums;

namespace be.Contracts.Requests;

public sealed class TicketSearchRequest : AccountPageRequest
{
    [StringLength(200)] public string? Search { get; init; }
    [EnumDataType(typeof(TicketStatus))] public TicketStatus? Status { get; init; }
    [EnumDataType(typeof(TicketPriority))] public TicketPriority? Priority { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? CategoryId { get; init; }
}
