using be.Domain.Entities;
using be.Domain.Enums;
using be.Domain.Rules;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class SlaRulesTests
{
    private static readonly DateTime Start = new(2028, 2, 28, 23, 0, 0, DateTimeKind.Utc);
    [Fact]
    public void SnapshotUsesUtcElapsedMinutesAcrossMidnightLeapDayAndPolicyChanges()
    {
        var ticket = new Ticket { Title = "Title", Description = "Description", CreatedAt = Start };
        var policy = new SlaPolicy { Name = "Normal", FirstResponseMinutes = 120, ResolutionMinutes = 1440 };
        var snapshot = SlaRules.Snapshot(ticket, policy);
        policy.Name = "Changed"; policy.FirstResponseMinutes = 1; policy.ResolutionMinutes = 2;
        Assert.Equal(new DateTime(2028, 2, 29, 1, 0, 0, DateTimeKind.Utc), snapshot.FirstResponseDueAt);
        Assert.Equal(Start.AddHours(24), snapshot.ResolutionDueAt);
        Assert.Equal("Normal", snapshot.PolicyName);
        Assert.Equal(120, snapshot.FirstResponseMinutes);
    }
    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(2, 1, false)]
    [InlineData(1, 525601, false)]
    [InlineData(1, 1, true)]
    [InlineData(525600, 525600, true)]
    public void TargetsAreBoundedAndResponseCannotExceedResolution(int response, int resolution, bool valid) => Assert.Equal(valid, SlaRules.ValidDurations(response, resolution));
    [Fact]
    public void EqualityIsOnTimeAndLateResponsesStayBreachedAfterCompletion()
    {
        var sla = new TicketSla { FirstResponseDueAt = Start.AddMinutes(1), ResolutionDueAt = Start.AddMinutes(2) };
        Assert.False(SlaRules.FirstResponseBreached(sla, TicketStatus.New, sla.FirstResponseDueAt));
        Assert.True(SlaRules.FirstResponseBreached(sla, TicketStatus.WaitingForRequester, sla.FirstResponseDueAt.AddTicks(10)));
        sla.FirstRespondedAt = Start.AddMinutes(1).AddTicks(10);
        sla.ResolvedAt = Start.AddMinutes(3);
        Assert.True(SlaRules.FirstResponseBreached(sla, TicketStatus.Resolved, Start.AddDays(1)));
        Assert.True(SlaRules.ResolutionBreached(sla, TicketStatus.Resolved, Start.AddDays(1)));
    }
    [Fact]
    public void ResolveStopsClocksReopenResumesOriginalDeadlinesAndTerminalStatusesAreSkipped()
    {
        var sla = new TicketSla { FirstResponseDueAt = Start.AddMinutes(1), ResolutionDueAt = Start.AddMinutes(2), ResolvedAt = Start };
        Assert.False(SlaRules.FirstResponseBreached(sla, TicketStatus.Resolved, Start.AddDays(1)));
        Assert.False(SlaRules.ResolutionBreached(sla, TicketStatus.Resolved, Start.AddDays(1)));
        sla.ResolvedAt = null;
        Assert.True(SlaRules.ResolutionBreached(sla, TicketStatus.Reopened, Start.AddDays(1)));
        Assert.False(SlaRules.ResolutionBreached(sla, TicketStatus.Closed, Start.AddDays(1)));
        Assert.False(SlaRules.ResolutionBreached(sla, TicketStatus.Cancelled, Start.AddDays(1)));
        sla.ResolutionBreachedAt = Start.AddMinutes(3);
        Assert.True(SlaRules.ResolutionBreached(sla, TicketStatus.Closed, Start.AddDays(1)));
    }
}
