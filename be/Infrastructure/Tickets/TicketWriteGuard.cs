using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Tickets;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Tickets;

// Use from Infrastructure mutation stores, inside their transaction, BEFORE changing the entity.
// The caller owns workflow validation, TicketActivity, SaveChanges and commit/rollback.
public sealed class TicketWriteGuard(HelpdeskDbContext database, TicketScope scope, IAuthSessionStore sessions)
{
    public Task<Ticket> LockAsync(AccountActor actor, Guid id, TicketOperation operation, uint version, CancellationToken cancellationToken)
    {
        if (operation is not (TicketOperation.UpdateStatus or TicketOperation.Assign or TicketOperation.Confirm or TicketOperation.Reopen or TicketOperation.Cancel) || version == 0)
            throw Error(TicketError.InvalidInput);
        return LockCoreAsync(actor, id, operation, version, cancellationToken);
    }

    // Immutable appends cannot overwrite a concurrent comment. The same row lock still orders
    // appends with status/assignment changes and rechecks their current permission/resource scope.
    public Task<Ticket> LockCommentAsync(AccountActor actor, Guid id, bool isInternal, CancellationToken cancellationToken) =>
        LockCoreAsync(actor, id, isInternal ? TicketOperation.CommentInternal : TicketOperation.CommentPublic, null, cancellationToken);

    private async Task<Ticket> LockCoreAsync(AccountActor actor, Guid id, TicketOperation operation, uint? version, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Ticket mutations require an explicit transaction before acquiring the write guard.");
        if (database.ChangeTracker.Entries<Ticket>().Any(x => x.Entity.Id == id && x.State != EntityState.Unchanged))
            throw new InvalidOperationException("Acquire the Ticket write guard before editing or deleting a tracked Ticket.");

        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
        var user = await database.Users.FromSqlInterpolated($"SELECT *, xmin FROM identity.users WHERE id = {actor.UserId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (user is null || !await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw Error(TicketError.Unauthenticated);
        if (!await scope.HasReadGrantAsync(actor, cancellationToken)) throw Error(TicketError.Forbidden);

        var ticket = await database.Tickets.FromSqlInterpolated($"SELECT *, xmin FROM helpdesk.tickets WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw Error(TicketError.NotFound);
        // Refresh a previously read, unchanged tracked instance after obtaining the row lock.
        await database.Entry(ticket).ReloadAsync(cancellationToken);
        if (!await scope.For(actor, TicketOperation.Read).AnyAsync(x => x.Id == id, cancellationToken)) throw Error(TicketError.NotFound);
        if (!await scope.For(actor, operation).AnyAsync(x => x.Id == id, cancellationToken)) throw Error(TicketError.Forbidden);
        if (version is not null && ticket.Version != version) throw Error(TicketError.VersionConflict);
        return ticket;
    }

    public async Task<Ticket> LockAssignmentAsync(AccountActor actor, Guid id, uint version, TicketAssignmentTarget target, CancellationToken cancellationToken)
    {
        var ticket = await LockAsync(actor, id, TicketOperation.Assign, version, cancellationToken);
        if (!await scope.CanAssignToAsync(actor, id, target, cancellationToken)) throw Error(TicketError.InvalidAssignment);
        return ticket;
    }
    private static TicketOperationException Error(TicketError error) => new(error);
}
