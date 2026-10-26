using be.Application.Features.Accounts;

namespace be.Application.Features.Tickets;

public interface ITicketAccessChecker
{
    Task<bool> CanAsync(AccountActor actor, Guid ticketId, TicketOperation operation, CancellationToken cancellationToken);
    Task<TicketAccess> GetAsync(AccountActor actor, Guid ticketId, CancellationToken cancellationToken);
    Task<bool> CanAssignToAsync(AccountActor actor, Guid ticketId, TicketAssignmentTarget target, CancellationToken cancellationToken);
}
