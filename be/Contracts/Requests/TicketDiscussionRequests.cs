using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using be.Application.Features.Tickets;

namespace be.Contracts.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTicketCommentRequest
{
    [Required, StringLength(TicketDiscussionService.ContentLimit)] public string Content { get; init; } = "";
    public bool IsInternal { get; init; }
}

public sealed class TicketDiscussionSearchRequest : AccountPageRequest
{
    public bool Descending { get; init; }
    public bool? IsInternal { get; init; }
}
