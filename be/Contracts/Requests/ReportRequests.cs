using System.ComponentModel.DataAnnotations;
using be.Domain.Enums;

namespace be.Contracts.Requests;

public class ReportFilterRequest
{
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? AgentId { get; init; }
    [EnumDataType(typeof(TicketStatus))] public TicketStatus? Status { get; init; }
    [EnumDataType(typeof(TicketPriority))] public TicketPriority? Priority { get; init; }
}
public sealed class GroupedReportRequest : ReportFilterRequest
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [RegularExpression("^(total|open|slaBreached)$")] public string SortBy { get; init; } = "total";
    public bool Descending { get; init; } = true;
}
