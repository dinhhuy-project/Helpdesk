using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Tickets;

public sealed class TicketAccessChecker(TicketScope scope) : ITicketAccessChecker
{
    public Task<bool> CanAsync(AccountActor actor, Guid ticketId, TicketOperation operation, CancellationToken cancellationToken) =>
        scope.For(actor, operation).AnyAsync(x => x.Id == ticketId, cancellationToken);

    public async Task<TicketAccess> GetAsync(AccountActor actor, Guid ticketId, CancellationToken cancellationToken)
    {
        if (!await CanAsync(actor, ticketId, TicketOperation.Read, cancellationToken)) return new(false, false, false);
        return new(true, await CanAsync(actor, ticketId, TicketOperation.UpdateStatus, cancellationToken),
            await CanAsync(actor, ticketId, TicketOperation.Assign, cancellationToken));
    }
    public Task<bool> CanAssignToAsync(AccountActor actor, Guid ticketId, TicketAssignmentTarget target, CancellationToken cancellationToken) =>
        scope.CanAssignToAsync(actor, ticketId, target, cancellationToken);
}
