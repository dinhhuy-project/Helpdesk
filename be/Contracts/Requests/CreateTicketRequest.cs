using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using be.Domain.Enums;

namespace be.Contracts.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTicketRequest
{
    [Required, StringLength(300)] public string Title { get; init; } = "";
    [Required, StringLength(20_000)] public string Description { get; init; } = "";
    public required Guid CategoryId { get; init; }
    [EnumDataType(typeof(TicketPriority))] public TicketPriority Priority { get; init; } = TicketPriority.Normal;
}
