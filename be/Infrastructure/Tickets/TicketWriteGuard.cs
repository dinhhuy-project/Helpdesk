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
    public async Task<Ticket> LockAsync(AccountActor actor, Guid id, TicketOperation operation, uint version, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Ticket mutations require an explicit transaction before acquiring the write guard.");
        if (operation is not (TicketOperation.UpdateStatus or TicketOperation.Assign) || version == 0)
            throw Error(TicketError.InvalidInput);
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
        if (ticket.Version != version) throw Error(TicketError.VersionConflict);
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
