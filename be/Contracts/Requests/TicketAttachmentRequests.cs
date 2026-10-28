using System.ComponentModel.DataAnnotations;

namespace be.Contracts.Requests;

public sealed class UploadTicketAttachmentRequest
{
    [Required] public IFormFile File { get; init; } = null!;
    public Guid? CommentId { get; init; }
    public bool? IsInternal { get; init; }
}
public sealed class TicketAttachmentSearchRequest : AccountPageRequest
{
    public Guid? CommentId { get; init; }
    public bool? IsInternal { get; init; }
    public bool Descending { get; init; }
}
