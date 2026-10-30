using be.Application.Features.Tickets;

namespace be.Application.Common.Storage;

public interface IAttachmentStorage
{
    string Bucket { get; }
    Task PutAsync(string bucket, string key, Stream source, long length, string contentType, CancellationToken cancellationToken);
    Task CopyToAsync(string bucket, string key, Stream destination, long expectedLength, CancellationToken cancellationToken);
    Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken);
}
public interface IAttachmentFileValidator
{
    AttachmentLimits Limits { get; }
    AttachmentUploadPolicy Policy { get; }
    Stream CreateDownloadBuffer();
    UploadTicketAttachment ValidateMetadata(UploadTicketAttachment command);
    Task<Stream> BufferAsync(Stream input, UploadTicketAttachment command, CancellationToken cancellationToken);
}

