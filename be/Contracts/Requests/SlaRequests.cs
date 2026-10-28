using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using be.Domain.Enums;
using be.Domain.Rules;

namespace be.Contracts.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class SaveSlaPolicyRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [EnumDataType(typeof(TicketPriority))] public required TicketPriority Priority { get; init; }
    [Range(1, SlaRules.MaximumMinutes)] public required int FirstResponseMinutes { get; init; }
    [Range(1, SlaRules.MaximumMinutes)] public required int ResolutionMinutes { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateSlaPolicyRequest : SaveSlaPolicyRequest
{
    [Range(typeof(uint), "1", "4294967295")] public required uint Version { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SlaPolicyStatusRequest : VersionedAccountRequest
{
    public required bool IsActive { get; init; }
}
