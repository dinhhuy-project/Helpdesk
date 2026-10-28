using System.Text.Json;
using be.Application.Common.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Categories;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Tickets;

public sealed class TicketCreateStore(HelpdeskDbContext database, IAuthSessionStore sessions,
    IPermissionChecker permissions, TimeProvider clock) : ITicketCreateStore
{
    public async Task<TicketCreated> CreateAsync(AccountActor actor, CreateTicket command, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Same order as account, organization and category administration: lock before validating.
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
        var user = await database.Users.FromSqlInterpolated($"SELECT *, xmin FROM identity.users WHERE id = {actor.UserId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (user is null || !await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken))
            throw new TicketOperationException(TicketError.Unauthenticated);
        if (!await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, PermissionCodes.TicketCreate, cancellationToken))
            throw new TicketOperationException(TicketError.Forbidden);
        if (!await TicketCategoryAvailability.Query(database).AnyAsync(id => id == command.CategoryId, cancellationToken))
            throw new TicketOperationException(TicketError.InvalidCategory);

        var now = clock.GetUtcNow().UtcDateTime;
        var ticket = new Ticket { Title = command.Title, Description = command.Description, CategoryId = command.CategoryId,
            RequesterId = actor.UserId, CreatedByUserId = actor.UserId, Priority = command.Priority, Status = TicketStatus.New, CreatedAt = now };
        database.Tickets.Add(ticket);
        // PostgreSQL alone generates TicketNumber. Keep both saves in this transaction so an
        // activity failure rolls back the Ticket while retaining normal identity sequence gaps.
        await database.SaveChangesAsync(cancellationToken);
        database.TicketActivities.Add(new TicketActivity { TicketId = ticket.Id, ActorId = actor.UserId,
            Action = TicketActivityActions.Created, CreatedAt = now, NewValue = JsonSerializer.Serialize(new
            {
                ticket.TicketNumber, ticket.RequesterId, ticket.CreatedByUserId, ticket.CategoryId,
                Status = ticket.Status.ToString(), Priority = ticket.Priority.ToString()
            }) });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // A creation receipt does not implicitly grant the caller read access to a Ticket.
        return new(ticket.Id, ticket.TicketNumber, ticket.RequesterId, ticket.CreatedByUserId, ticket.CategoryId,
            ticket.Status, ticket.Priority, ticket.CreatedAt, ticket.Version);
    }
}
