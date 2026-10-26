using be.Domain.Entities;
using be.Domain.Enums;
using be.Domain.Rules;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class TicketWorkflowTests
{
    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.WaitingForRequester)]
    [InlineData(TicketStatus.WaitingForRequester, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Reopened)]
    [InlineData(TicketStatus.Reopened, TicketStatus.InProgress)]
    [InlineData(TicketStatus.New, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.WaitingForRequester, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.Reopened, TicketStatus.Cancelled)]
    public void AcceptedTransitionsSetServerTimeAndPreserveOrClearResolutionAsRequired(TicketStatus from, TicketStatus to)
    {
        var ticket = Ticket(from);
        var resolved = ticket.ResolvedAt;
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        TicketWorkflow.Apply(ticket, to, to == TicketStatus.Resolved ? "  Result  " : null, now);
        Assert.Equal(to, ticket.Status);
        Assert.Equal(now, ticket.UpdatedAt);
        if (to == TicketStatus.Resolved)
        {
            Assert.Equal("Result", ticket.ResolutionSummary);
            Assert.Equal(now, ticket.ResolvedAt);
        }
        if (to == TicketStatus.Closed)
        {
            Assert.Equal(now, ticket.ClosedAt);
            Assert.Equal(resolved, ticket.ResolvedAt);
            Assert.Equal("Prior result", ticket.ResolutionSummary);
        }
        if (to == TicketStatus.Reopened)
        {
            Assert.Null(ticket.ResolutionSummary);
            Assert.Null(ticket.ResolvedAt);
            Assert.Null(ticket.ClosedAt);
        }
    }

    [Fact]
    public void TerminalStatesCannotLeaveAndEveryOtherUnlistedEdgeFailsWithoutMutation()
    {
        var allowed = new Dictionary<TicketStatus, TicketStatus[]>
        {
            [TicketStatus.New] = [TicketStatus.InProgress, TicketStatus.Cancelled],
            [TicketStatus.InProgress] = [TicketStatus.WaitingForRequester, TicketStatus.Resolved, TicketStatus.Cancelled],
            [TicketStatus.WaitingForRequester] = [TicketStatus.InProgress, TicketStatus.Cancelled],
            [TicketStatus.Resolved] = [TicketStatus.Closed, TicketStatus.Reopened],
            [TicketStatus.Reopened] = [TicketStatus.InProgress, TicketStatus.Cancelled]
        };
        foreach (var from in Enum.GetValues<TicketStatus>())
        foreach (var to in Enum.GetValues<TicketStatus>().Append((TicketStatus)999))
        {
            if (allowed.TryGetValue(from, out var targets) && targets.Contains(to)) continue;
            var ticket = Ticket(from);
            var error = Assert.Throws<TicketWorkflowException>(() => TicketWorkflow.Apply(ticket, to, null, DateTime.UtcNow));
            Assert.Equal(TicketWorkflowError.InvalidTransition, error.Error);
            Assert.Equal(from, ticket.Status);
            Assert.Null(ticket.UpdatedAt);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveNeedsANonWhitespaceResult(string? summary)
    {
        var ticket = Ticket(TicketStatus.InProgress);
        Assert.Equal(TicketWorkflowError.ResolutionRequired,
            Assert.Throws<TicketWorkflowException>(() => TicketWorkflow.Apply(ticket, TicketStatus.Resolved, summary, DateTime.UtcNow)).Error);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
    }

    private static Ticket Ticket(TicketStatus status) => new()
    {
        Title = "Title", Description = "Description", Status = status,
        ResolutionSummary = status == TicketStatus.Resolved ? "Prior result" : null,
        ResolvedAt = status == TicketStatus.Resolved ? DateTime.UtcNow.AddDays(-1) : null
    };
}
