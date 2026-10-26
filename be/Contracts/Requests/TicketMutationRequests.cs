using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using be.Domain.Enums;
using be.Domain.Rules;

namespace be.Contracts.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AssignTicketRequest : VersionedAccountRequest
{
    public required Guid TeamId { get; init; }
    public Guid? AgentId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TransitionTicketRequest : VersionedAccountRequest
{
    [EnumDataType(typeof(TicketStatus))] public required TicketStatus Status { get; init; }
    [StringLength(TicketWorkflow.ResolutionSummaryLimit)] public string? ResolutionSummary { get; init; }
}
