using be.Domain.Enums;

namespace be.Application.Features.Tickets;

public sealed record CreateTicket(string Title, string Description, Guid CategoryId, TicketPriority Priority = TicketPriority.Normal);
public sealed record TicketCreated(Guid Id, long TicketNumber, Guid RequesterId, Guid CreatedByUserId,
    Guid CategoryId, TicketStatus Status, TicketPriority Priority, DateTime CreatedAt, uint Version);
