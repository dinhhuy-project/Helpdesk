using be.Application.Features.Accounts;

namespace be.Application.Features.Tickets;

public interface ITicketCreateStore
{
    Task<TicketCreated> CreateAsync(AccountActor actor, CreateTicket command, CancellationToken cancellationToken);
}
