using be.Application.Features.Accounts;
using be.Domain.Enums;

namespace be.Application.Features.Reporting;

public sealed record ReportFilter(DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null,
    Guid? TeamId = null, Guid? AgentId = null, TicketStatus? Status = null, TicketPriority? Priority = null);
public sealed record ReportPeriod(DateTime CreatedFrom, DateTime CreatedTo, DateTime AsOf);
public sealed record ReportPage(int Page = 1, int PageSize = 20, string SortBy = "total", bool Descending = true);
public sealed record ReportCount(string Key, long Count);
public sealed record SlaMetricReport(long Met, long Breached, long Pending, long Stopped, long Unknown,
    long Measured, decimal? MetPercent, long DurationSamples, double? AverageMinutes);
public sealed record SlaReport(long WithSla, long WithoutSla, long ExcludedCancelled, long Eligible,
    long BreachedTickets, SlaMetricReport FirstResponse, SlaMetricReport Resolution);
public sealed record TicketReportTotals(long Total, long Open, long Resolved, long Closed, long Cancelled,
    long UnassignedTeam, long UnassignedAgent);
public sealed record DashboardReport(ReportPeriod Period, TicketReportTotals Tickets, IReadOnlyList<ReportCount> ByStatus,
    IReadOnlyList<ReportCount> ByPriority, SlaReport Sla);
public sealed record TicketGroupReport(Guid? Id, string Name, bool? IsActive, TicketReportTotals Tickets, SlaReport Sla);
public sealed record GroupedTicketReport(ReportPeriod Period, PagedResult<TicketGroupReport> Groups);
public sealed record DailyTicketCount(DateTime Date, long CreatedTickets);
public sealed record DailyTicketReport(ReportPeriod Period, IReadOnlyList<DailyTicketCount> Days);

public interface IReportStore
{
    Task<DashboardReport> DashboardAsync(AccountActor actor, ReportFilter filter, ReportPeriod period, CancellationToken cancellationToken);
    Task<GroupedTicketReport> GroupsAsync(AccountActor actor, ReportFilter filter, ReportPeriod period, ReportPage page, bool byAgent, CancellationToken cancellationToken);
    Task<DailyTicketReport> DailyAsync(AccountActor actor, ReportFilter filter, ReportPeriod period, CancellationToken cancellationToken);
}

public sealed class ReportService(IReportStore store, TimeProvider clock)
{
    public Task<DashboardReport> DashboardAsync(AccountActor actor, ReportFilter filter, CancellationToken cancellationToken) =>
        store.DashboardAsync(actor, filter, Period(filter), cancellationToken);
    public Task<GroupedTicketReport> GroupsAsync(AccountActor actor, ReportFilter filter, ReportPage page, bool byAgent, CancellationToken cancellationToken)
    {
        if (page.Page is < 1 or > 1_000_000 || page.PageSize is < 1 or > 100 || page.SortBy is not ("total" or "open" or "slaBreached"))
            throw new AccountOperationException(AccountError.InvalidInput);
        return store.GroupsAsync(actor, filter, Period(filter), page, byAgent, cancellationToken);
    }
    public Task<DailyTicketReport> DailyAsync(AccountActor actor, ReportFilter filter, CancellationToken cancellationToken) =>
        store.DailyAsync(actor, filter, Period(filter), cancellationToken);
    private ReportPeriod Period(ReportFilter filter)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var from = filter.CreatedFrom?.UtcDateTime ?? now.Date.AddDays(-29);
        var to = filter.CreatedTo?.UtcDateTime ?? now;
        if (from >= to || to - from > TimeSpan.FromDays(366) || filter.TeamId == Guid.Empty || filter.AgentId == Guid.Empty ||
            (filter.Status is not null && !Enum.IsDefined(filter.Status.Value)) || (filter.Priority is not null && !Enum.IsDefined(filter.Priority.Value)))
            throw new AccountOperationException(AccountError.InvalidInput);
        return new(from, to, now);
    }
}
