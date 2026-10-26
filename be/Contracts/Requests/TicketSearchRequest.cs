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
    [Range(typeof(long), "1", "9223372036854775807")] public long? TicketNumber { get; init; }
    public Guid? RequesterId { get; init; }
    public Guid? AgentId { get; init; }
    public bool? Unassigned { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    [RegularExpression("^(createdAt|updatedAt|ticketNumber|priority)$")] public string SortBy { get; init; } = "createdAt";
    public bool Descending { get; init; } = true;
}
