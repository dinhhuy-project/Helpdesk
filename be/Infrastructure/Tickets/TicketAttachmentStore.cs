using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using be.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Tickets;

public sealed class TicketAttachmentStore(HelpdeskDbContext database, TicketScope scope, TicketWriteGuard guard,
    IAuthSessionStore sessions, TimeProvider clock, TicketNotificationWriter notifications) : ITicketAttachmentStore
{
    public async Task<TicketAttachmentAccess> AccessAsync(AccountActor actor, Guid ticketId, Guid? commentId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        await RequireReadAsync(actor, ticketId, cancellationToken);
        var comment = await CommentAsync(actor, ticketId, commentId, cancellationToken);
        var own = comment is null || comment.AuthorId == actor.UserId;
        var canReadInternal = await scope.For(actor, TicketOperation.ReadInternal).AnyAsync(x => x.Id == ticketId, cancellationToken);
        var publicWrite = own && (comment is null || !comment.IsInternal) &&
            await scope.For(actor, TicketOperation.CommentPublic).AnyAsync(x => x.Id == ticketId, cancellationToken);
        var internalWrite = own && (comment is null || comment.IsInternal) &&
            await scope.For(actor, TicketOperation.CommentInternal).AnyAsync(x => x.Id == ticketId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(canReadInternal, publicWrite, internalWrite);
    }
    public async Task<AttachmentReservation> ReserveAsync(AccountActor actor, Guid ticketId, UploadTicketAttachment command,
        string bucket, AttachmentLimits limits, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
        var comment = await CommentAsync(actor, ticketId, command.CommentId, cancellationToken);
        var isInternal = comment?.IsInternal ?? command.IsInternal ?? false;
        await guard.LockCommentAsync(actor, ticketId, isInternal, cancellationToken);
        if (comment is not null && comment.AuthorId != actor.UserId) throw Error(TicketError.Forbidden);
        if (comment is not null && command.IsInternal is not null && command.IsInternal != comment.IsInternal) throw Error(TicketError.InvalidInput);
        // Separate visibility budgets avoid revealing hidden file counts through public quotas.
        var files = database.TicketAttachments.Where(x => x.TicketId == ticketId && x.IsInternal == isInternal);
        var pending = database.AttachmentUploadIntents.Where(x => x.TicketId == ticketId && x.IsInternal == isInternal);
        var count = await files.CountAsync(cancellationToken) + await pending.CountAsync(cancellationToken);
        var bytes = (await files.SumAsync(x => (long?)x.SizeBytes, cancellationToken) ?? 0) +
            (await pending.SumAsync(x => (long?)x.SizeBytes, cancellationToken) ?? 0);
        if (count >= limits.MaxFiles || command.SizeBytes > limits.MaxBytes - bytes) throw Error(TicketError.AttachmentQuotaExceeded);
        var now = clock.GetUtcNow().UtcDateTime;
        var intent = new AttachmentUploadIntent { TicketId = ticketId, CommentId = command.CommentId, UploadedByUserId = actor.UserId,
            Bucket = bucket, ObjectKey = "attachments/" + Guid.NewGuid().ToString("N"), FileName = command.FileName,
            ContentType = command.ContentType, SizeBytes = command.SizeBytes, IsInternal = isInternal,
            CreatedAt = now, ExpiresAt = now.AddMinutes(15) };
        database.AttachmentUploadIntents.Add(intent);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.Entry(intent).State = EntityState.Detached;
        return new(intent.Id, intent.Bucket, intent.ObjectKey);
    }
    public async Task<TicketAttachmentDetails> CompleteAsync(AccountActor actor, Guid ticketId, Guid reservationId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
        var intent = await database.AttachmentUploadIntents.SingleOrDefaultAsync(x => x.Id == reservationId &&
            x.TicketId == ticketId && x.UploadedByUserId == actor.UserId, cancellationToken) ?? throw Error(TicketError.UploadExpired);
        await database.Entry(intent).ReloadAsync(cancellationToken);
        if (intent.IsDeleting || intent.ExpiresAt <= clock.GetUtcNow().UtcDateTime) throw Error(TicketError.UploadExpired);
        await guard.LockCommentAsync(actor, ticketId, intent.IsInternal, cancellationToken);
        var comment = await CommentAsync(actor, ticketId, intent.CommentId, cancellationToken);
        if (comment is not null && (comment.IsInternal != intent.IsInternal || comment.AuthorId != actor.UserId)) throw Error(TicketError.Forbidden);
        var now = clock.GetUtcNow().UtcDateTime;
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var file = new TicketAttachment { Id = intent.Id, TicketId = ticketId, CommentId = intent.CommentId,
            UploadedByUserId = actor.UserId, Bucket = intent.Bucket, ObjectKey = intent.ObjectKey, FileName = intent.FileName,
            ContentType = intent.ContentType, SizeBytes = intent.SizeBytes, IsInternal = intent.IsInternal, CreatedAt = now };
        database.TicketAttachments.Add(file);
        database.TicketActivities.Add(new TicketActivity { TicketId = ticketId, ActorId = actor.UserId, CreatedAt = now,
            Action = file.IsInternal ? TicketActivityActions.InternalAttachmentAdded : TicketActivityActions.PublicAttachmentAdded,
            NewValue = JsonSerializer.Serialize(new { AttachmentId = file.Id, file.CommentId, file.IsInternal }) });
        database.AttachmentUploadIntents.Remove(intent);
        await notifications.SaveChangesAsync(actor.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Details(file);
    }
    public async Task<PagedResult<TicketAttachmentDetails>> ListAsync(AccountActor actor, Guid ticketId, TicketDiscussionSearch search,
        Guid? commentId, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, ticketId, cancellationToken);
        await CommentAsync(actor, ticketId, commentId, cancellationToken);
        var query = Visible(actor, ticketId);
        if (commentId is not null) query = query.Where(x => x.CommentId == commentId);
        if (search.IsInternal is not null) query = query.Where(x => x.IsInternal == search.IsInternal);
        var count = await query.CountAsync(cancellationToken);
        var ordered = search.Descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt);
        var rows = await ordered.ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(x => new TicketAttachmentDetails(x.Id, x.TicketId, x.CommentId, x.UploadedByUserId, x.FileName,
                x.ContentType, x.SizeBytes, x.IsInternal, x.CreatedAt)).ToListAsync(cancellationToken);
        return new(rows, search.Page, search.PageSize, count);
    }
    public async Task<AttachmentObject> GetAsync(AccountActor actor, Guid ticketId, Guid attachmentId, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, ticketId, cancellationToken);
        var row = await Visible(actor, ticketId).SingleOrDefaultAsync(x => x.Id == attachmentId, cancellationToken) ?? throw Error(TicketError.NotFound);
        return new(Details(row), row.Bucket, row.ObjectKey);
    }
    private IQueryable<TicketAttachment> Visible(AccountActor actor, Guid ticketId)
    {
        var readable = scope.For(actor, TicketOperation.Read).Select(x => x.Id);
        var internalReadable = scope.For(actor, TicketOperation.ReadInternal).Select(x => x.Id);
        return database.TicketAttachments.AsNoTracking().Where(file => file.TicketId == ticketId && readable.Contains(file.TicketId) &&
            (!file.IsInternal || internalReadable.Contains(file.TicketId)) && (file.CommentId == null ||
                database.TicketComments.Any(comment => comment.Id == file.CommentId && comment.TicketId == file.TicketId && comment.IsInternal == file.IsInternal)));
    }
    private async Task<TicketComment?> CommentAsync(AccountActor actor, Guid ticketId, Guid? commentId, CancellationToken cancellationToken)
    {
        if (commentId is null) return null;
        return await scope.CommentsFor(actor).SingleOrDefaultAsync(x => x.Id == commentId && x.TicketId == ticketId, cancellationToken)
            ?? throw Error(TicketError.NotFound);
    }
    private async Task RequireReadAsync(AccountActor actor, Guid ticketId, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw Error(TicketError.Unauthenticated);
        if (!await scope.HasReadGrantAsync(actor, cancellationToken)) throw Error(TicketError.Forbidden);
        if (!await scope.For(actor, TicketOperation.Read).AnyAsync(x => x.Id == ticketId, cancellationToken)) throw Error(TicketError.NotFound);
    }
    private static TicketAttachmentDetails Details(TicketAttachment file) => new(file.Id, file.TicketId, file.CommentId, file.UploadedByUserId,
        file.FileName, file.ContentType, file.SizeBytes, file.IsInternal, file.CreatedAt);
    private static TicketOperationException Error(TicketError error) => new(error);
}
