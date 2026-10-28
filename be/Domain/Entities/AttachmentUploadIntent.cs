namespace be.Domain.Entities;

// Durable reservation exists BEFORE storage I/O. A cleanup claim prevents late finalization.
public class AttachmentUploadIntent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Guid? CommentId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public required string Bucket { get; set; }
    public required string ObjectKey { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public bool IsInternal { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsDeleting { get; set; }
}
