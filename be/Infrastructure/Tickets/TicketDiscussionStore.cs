using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using be.Infrastructure.Notifications;
using be.Infrastructure.Sla;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Tickets;

public sealed class TicketDiscussionStore(HelpdeskDbContext database, TicketScope scope, TicketWriteGuard guard,
    IAuthSessionStore sessions, TimeProvider clock, TicketNotificationWriter notifications, TicketSlaTracker sla) : ITicketDiscussionStore
{
    public async Task<TicketCommentDetails> CreateAsync(AccountActor actor, Guid ticketId, CreateTicketComment command, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var ticket = await guard.LockCommentAsync(actor, ticketId, command.IsInternal, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var comment = new TicketComment { TicketId = ticketId, AuthorId = actor.UserId, Content = command.Content,
            IsInternal = command.IsInternal, CreatedAt = now };
        database.TicketComments.Add(comment);
        if (!command.IsInternal && ticket.RequesterId != actor.UserId &&
            await scope.For(actor, TicketOperation.UpdateStatus).AnyAsync(x => x.Id == ticketId, cancellationToken))
            await sla.FirstResponseAsync(ticket, actor.UserId, now, cancellationToken);
        database.TicketActivities.Add(new TicketActivity { TicketId = ticketId, ActorId = actor.UserId, CreatedAt = now,
            Action = command.IsInternal ? TicketActivityActions.InternalNoteAdded : TicketActivityActions.PublicCommentAdded,
            NewValue = JsonSerializer.Serialize(new { CommentId = comment.Id, comment.IsInternal }) });
        await notifications.SaveChangesAsync(actor.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(comment.Id, comment.TicketId, comment.AuthorId, comment.Content, comment.IsInternal, comment.CreatedAt, comment.UpdatedAt);
    }

    public async Task<PagedResult<TicketCommentDetails>> ListCommentsAsync(AccountActor actor, Guid ticketId, TicketDiscussionSearch search, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, ticketId, cancellationToken);
        var query = Comments(actor, ticketId);
        if (search.IsInternal is not null) query = query.Where(comment => comment.IsInternal == search.IsInternal);
        var count = await query.CountAsync(cancellationToken);
        var ordered = search.Descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt);
        var items = await Project(ordered.ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)).ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }
    public async Task<TicketCommentDetails> GetCommentAsync(AccountActor actor, Guid ticketId, Guid commentId, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, ticketId, cancellationToken);
        return await Project(Comments(actor, ticketId).Where(x => x.Id == commentId)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new TicketOperationException(TicketError.NotFound);
    }
    public async Task<PagedResult<TicketActivityDetails>> ListActivitiesAsync(AccountActor actor, Guid ticketId, TicketDiscussionSearch search, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, ticketId, cancellationToken);
        var query = Activities(actor, ticketId);
        if (search.IsInternal is not null) query = query.Where(x =>
            (x.Action == TicketActivityActions.InternalNoteAdded || x.Action == TicketActivityActions.InternalAttachmentAdded) == search.IsInternal);
        var count = await query.CountAsync(cancellationToken);
        var ordered = search.Descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt);
        var items = await ordered.ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(cancellationToken);
        return new(items.Select(TicketActivityProjection.Map).ToArray(), search.Page, search.PageSize, count);
    }
    public async Task<TicketActivityDetails> GetActivityAsync(AccountActor actor, Guid ticketId, Guid activityId, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, ticketId, cancellationToken);
        var activity = await Activities(actor, ticketId).SingleOrDefaultAsync(x => x.Id == activityId, cancellationToken)
            ?? throw new TicketOperationException(TicketError.NotFound);
        return TicketActivityProjection.Map(activity);
    }

    // These queries reapply the shared session/resource scope, including when a grant is
    // removed after the initial check. Visibility is applied before count/page/projection.
    private IQueryable<TicketComment> Comments(AccountActor actor, Guid ticketId)
    {
        return scope.CommentsFor(actor).Where(comment => comment.TicketId == ticketId);
    }

    private IQueryable<TicketActivity> Activities(AccountActor actor, Guid ticketId)
    {
        var readable = scope.For(actor, TicketOperation.Read).Select(ticket => ticket.Id);
        var internalReadable = scope.For(actor, TicketOperation.ReadInternal).Select(ticket => ticket.Id);
        return database.TicketActivities.AsNoTracking().Where(activity => activity.TicketId == ticketId && readable.Contains(activity.TicketId) &&
            (activity.Action == TicketActivityActions.Created || activity.Action == TicketActivityActions.Assigned ||
             activity.Action == TicketActivityActions.StatusChanged || activity.Action == TicketActivityActions.PublicCommentAdded ||
             activity.Action == TicketActivityActions.PublicAttachmentAdded ||
             activity.Action == TicketActivityActions.SlaFirstResponseBreached || activity.Action == TicketActivityActions.SlaResolutionBreached ||
             ((activity.Action == TicketActivityActions.InternalNoteAdded || activity.Action == TicketActivityActions.InternalAttachmentAdded) &&
              internalReadable.Contains(activity.TicketId))));
    }

    private static IQueryable<TicketCommentDetails> Project(IQueryable<TicketComment> query) => query.Select(comment =>
        new TicketCommentDetails(comment.Id, comment.TicketId, comment.AuthorId, comment.Content, comment.IsInternal, comment.CreatedAt, comment.UpdatedAt));
    private async Task RequireReadAsync(AccountActor actor, Guid ticketId, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw new TicketOperationException(TicketError.Unauthenticated);
        if (!await scope.HasReadGrantAsync(actor, cancellationToken)) throw new TicketOperationException(TicketError.Forbidden);
        if (!await scope.For(actor, TicketOperation.Read).AnyAsync(x => x.Id == ticketId, cancellationToken)) throw new TicketOperationException(TicketError.NotFound);
    }
}
