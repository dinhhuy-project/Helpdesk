using be.Application.Features.Accounts;

namespace be.Application.Features.Tickets;

public sealed class TicketCreateService(ITicketCreateStore store)
{
    public Task<TicketCreated> CreateAsync(AccountActor actor, CreateTicket command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 300 ||
            string.IsNullOrWhiteSpace(command.Description) || command.Description.Length > 20_000 ||
            command.CategoryId == Guid.Empty || !Enum.IsDefined(command.Priority))
            throw new TicketOperationException(TicketError.InvalidInput);
        return store.CreateAsync(actor, command with { Title = command.Title.Trim(), Description = command.Description.Trim() }, cancellationToken);
    }
}
