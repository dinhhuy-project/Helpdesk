using be.Application.Features.Accounts;
using be.Application.Common.Storage;

namespace be.Application.Features.Tickets;

public sealed record UploadTicketAttachment(string FileName, string ContentType, long SizeBytes, Guid? CommentId = null, bool? IsInternal = null);
public sealed record TicketAttachmentDetails(Guid Id, Guid TicketId, Guid? CommentId, Guid UploadedByUserId,
    string FileName, string ContentType, long SizeBytes, bool IsInternal, DateTime CreatedAt);
public sealed record AttachmentObject(TicketAttachmentDetails Details, string Bucket, string ObjectKey);
public sealed record AttachmentReservation(Guid Id, string Bucket, string ObjectKey);
public sealed record AttachmentLimits(int MaxFiles, long MaxBytes);
public sealed record AttachmentDownload(TicketAttachmentDetails Details, Stream Content);
public sealed record AttachmentFileType(string Extension, string ContentType);
public sealed record AttachmentUploadPolicy(bool Enabled, long MaxFileSizeBytes, int MaxFilesPerVisibility,
    long MaxTotalSizeBytesPerVisibility, IReadOnlyList<AttachmentFileType> FileTypes);
public sealed record TicketAttachmentAccess(bool CanReadInternal, bool CanUploadPublic, bool CanUploadInternal);
public sealed record TicketAttachmentOptions(TicketAttachmentAccess Access, AttachmentUploadPolicy Policy);

public interface ITicketAttachmentStore
{
    Task<TicketAttachmentAccess> AccessAsync(AccountActor actor, Guid ticketId, Guid? commentId, CancellationToken cancellationToken);
    Task<AttachmentReservation> ReserveAsync(AccountActor actor, Guid ticketId, UploadTicketAttachment command, string bucket,
        AttachmentLimits limits, CancellationToken cancellationToken);
    Task<TicketAttachmentDetails> CompleteAsync(AccountActor actor, Guid ticketId, Guid reservationId, CancellationToken cancellationToken);
    Task<PagedResult<TicketAttachmentDetails>> ListAsync(AccountActor actor, Guid ticketId, TicketDiscussionSearch search, Guid? commentId, CancellationToken cancellationToken);
    Task<AttachmentObject> GetAsync(AccountActor actor, Guid ticketId, Guid attachmentId, CancellationToken cancellationToken);
}

public sealed class TicketAttachmentService(ITicketAttachmentStore store, IAttachmentStorage storage, IAttachmentFileValidator files)
{
    public async Task<TicketAttachmentOptions> OptionsAsync(AccountActor actor, Guid ticketId, Guid? commentId, CancellationToken cancellationToken)
    {
        if (ticketId == Guid.Empty || commentId == Guid.Empty) throw new TicketOperationException(TicketError.InvalidInput);
        var access = await store.AccessAsync(actor, ticketId, commentId, cancellationToken);
        var policy = files.Policy;
        return new(access with { CanUploadPublic = policy.Enabled && access.CanUploadPublic,
            CanUploadInternal = policy.Enabled && access.CanUploadInternal }, policy);
    }
    public async Task<TicketAttachmentDetails> UploadAsync(AccountActor actor, Guid ticketId, UploadTicketAttachment command, Stream input, CancellationToken cancellationToken)
    {
        if (ticketId == Guid.Empty || command.CommentId == Guid.Empty) throw new TicketOperationException(TicketError.InvalidInput);
        var validated = files.ValidateMetadata(command);
        await using var buffer = await files.BufferAsync(input, validated, cancellationToken);
        // Start the bounded I/O window before reservation: the 15-minute durable lease always
        // outlives this two-minute operation, including waits/retries. No late Put after cleanup.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var reservation = await store.ReserveAsync(actor, ticketId, validated, storage.Bucket, files.Limits, deadline.Token);
            await storage.PutAsync(reservation.Bucket, reservation.ObjectKey, buffer, validated.SizeBytes, validated.ContentType, deadline.Token);
            return await store.CompleteAsync(actor, ticketId, reservation.Id, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TicketOperationException(TicketError.StorageUnavailable); }
    }
    public Task<PagedResult<TicketAttachmentDetails>> ListAsync(AccountActor actor, Guid ticketId, TicketDiscussionSearch search, Guid? commentId, CancellationToken cancellationToken)
    {
        if (ticketId == Guid.Empty || commentId == Guid.Empty || search.Page is < 1 or > 1_000_000 || search.PageSize is < 1 or > 100)
            throw new TicketOperationException(TicketError.InvalidInput);
        return store.ListAsync(actor, ticketId, search, commentId, cancellationToken);
    }
    public async Task<TicketAttachmentDetails> GetAsync(AccountActor actor, Guid ticketId, Guid attachmentId, CancellationToken cancellationToken) =>
        (await store.GetAsync(actor, ticketId, attachmentId, cancellationToken)).Details;
    public async Task<AttachmentDownload> DownloadAsync(AccountActor actor, Guid ticketId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var reference = await store.GetAsync(actor, ticketId, attachmentId, cancellationToken);
        var buffer = files.CreateDownloadBuffer();
        try
        {
            await storage.CopyToAsync(reference.Bucket, reference.ObjectKey, buffer, reference.Details.SizeBytes, cancellationToken);
            // A revoked grant or membership during storage I/O must not authorize the response.
            await store.GetAsync(actor, ticketId, attachmentId, cancellationToken);
            buffer.Position = 0;
            return new(reference.Details, buffer);
        }
        catch { await buffer.DisposeAsync(); throw; }
    }
}
