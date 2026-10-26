using be.Domain.Entities;
using be.Domain.Enums;

namespace be.Domain.Rules;

public enum TicketWorkflowError { InvalidTransition, ResolutionRequired, InvalidInput }
public sealed class TicketWorkflowException(TicketWorkflowError error) : Exception("Ticket workflow rejected the transition.")
{
    public TicketWorkflowError Error { get; } = error;
}

public static class TicketWorkflow
{
    public const int ResolutionSummaryLimit = 4_000;

    public static bool CanTransition(TicketStatus from, TicketStatus to) => (from, to) switch
    {
        (TicketStatus.New, TicketStatus.InProgress or TicketStatus.Cancelled) => true,
        (TicketStatus.InProgress, TicketStatus.WaitingForRequester or TicketStatus.Resolved or TicketStatus.Cancelled) => true,
        (TicketStatus.WaitingForRequester, TicketStatus.InProgress or TicketStatus.Cancelled) => true,
        (TicketStatus.Resolved, TicketStatus.Closed or TicketStatus.Reopened) => true,
        (TicketStatus.Reopened, TicketStatus.InProgress or TicketStatus.Cancelled) => true,
        _ => false
    };

    // Authorization and current assignment eligibility are checked by the use case before persisting.
    public static void Apply(Ticket ticket, TicketStatus target, string? resolutionSummary, DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc) throw new ArgumentException("Workflow timestamps must be UTC.", nameof(now));
        if (!CanTransition(ticket.Status, target)) throw new TicketWorkflowException(TicketWorkflowError.InvalidTransition);
        if (resolutionSummary?.Length > ResolutionSummaryLimit || (target != TicketStatus.Resolved && resolutionSummary is not null))
            throw new TicketWorkflowException(TicketWorkflowError.InvalidInput);
        if (target == TicketStatus.Resolved && string.IsNullOrWhiteSpace(resolutionSummary))
            throw new TicketWorkflowException(TicketWorkflowError.ResolutionRequired);

        ticket.Status = target;
        ticket.UpdatedAt = now;
        if (target == TicketStatus.Resolved)
        {
            ticket.ResolutionSummary = resolutionSummary!.Trim();
            ticket.ResolvedAt = now;
            ticket.ClosedAt = null;
        }
        else if (target == TicketStatus.Closed) ticket.ClosedAt = now;
        else if (target == TicketStatus.Reopened)
        {
            ticket.ResolutionSummary = null;
            ticket.ResolvedAt = null;
            ticket.ClosedAt = null;
        }
    }
}
