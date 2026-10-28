using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Reporting;
using be.Infrastructure.Persistence;
using be.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Reporting;

public sealed class ReportStore(HelpdeskDbContext database, TicketScope scope, IAuthSessionStore sessions) : IReportStore
{
    public async Task<DashboardReport> DashboardAsync(AccountActor actor, ReportFilter filter, ReportPeriod period, CancellationToken cancellationToken)
    {
        var signature = await StartAsync(actor, cancellationToken);
        // Parameterize the single bucket: an untyped UUID SQL literal can be returned as text by PostgreSQL.
        Guid? bucket = actor.UserId;
        var row = await ReportQueries.Tickets(database, scope, actor, filter, period).GroupBy(_ => bucket)
            .Select(ReportQueries.Aggregate).TagWith("Reports:dashboard").SingleOrDefaultAsync(cancellationToken) ?? new ReportAggregate();
        await FinishAsync(actor, signature, cancellationToken);
        return new(period, Totals(row),
            [new("New", row.New), new("InProgress", row.InProgress), new("WaitingForRequester", row.Waiting),
             new("Resolved", row.Resolved), new("Closed", row.Closed), new("Cancelled", row.Cancelled), new("Reopened", row.Reopened)],
            [new("Low", row.Low), new("Normal", row.Normal), new("High", row.High), new("Urgent", row.Urgent)], Sla(row));
    }

    public async Task<GroupedTicketReport> GroupsAsync(AccountActor actor, ReportFilter filter, ReportPeriod period, ReportPage page, bool byAgent, CancellationToken cancellationToken)
    {
        var signature = await StartAsync(actor, cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        var tickets = ReportQueries.Tickets(database, scope, actor, filter, period);
        var groups = (byAgent ? tickets.GroupBy(x => x.AgentId) : tickets.GroupBy(x => x.TeamId)).Select(ReportQueries.Aggregate);
        var count = await groups.TagWith("Reports:groups-count").CountAsync(cancellationToken);
        var ordered = (page.SortBy, page.Descending) switch
        {
            ("open", false) => groups.OrderBy(x => x.Open), ("open", true) => groups.OrderByDescending(x => x.Open),
            ("slaBreached", false) => groups.OrderBy(x => x.BreachedTickets), ("slaBreached", true) => groups.OrderByDescending(x => x.BreachedTickets),
            (_, false) => groups.OrderBy(x => x.Total), _ => groups.OrderByDescending(x => x.Total)
        };
        var rows = await ordered.ThenBy(x => x.Id ?? Guid.Empty).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .TagWith("Reports:groups-page").ToListAsync(cancellationToken);
        var ids = rows.Where(x => x.Id != null).Select(x => x.Id!.Value).ToArray();
        // Resolve only IDs on the already scoped page; never return a directory of empty/hidden teams or agents.
        var names = byAgent
            ? await database.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new GroupName(x.Id, x.DisplayName, x.IsActive)).ToDictionaryAsync(x => x.Id, cancellationToken)
            : await database.SupportTeams.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new GroupName(x.Id, x.Name, x.IsActive)).ToDictionaryAsync(x => x.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await FinishAsync(actor, signature, cancellationToken);
        return new(period, new(rows.Select(x => new TicketGroupReport(x.Id, x.Id is { } id ? names[id].Name : "Chưa phân công",
            x.Id is { } key ? names[key].IsActive : null, Totals(x), Sla(x))).ToArray(), page.Page, page.PageSize, count));
    }

    public async Task<DailyTicketReport> DailyAsync(AccountActor actor, ReportFilter filter, ReportPeriod period, CancellationToken cancellationToken)
    {
        var signature = await StartAsync(actor, cancellationToken);
        var rows = await ReportQueries.Tickets(database, scope, actor, filter, period).GroupBy(x => x.CreatedAt.Date)
            .Select(group => new { Date = group.Key, CreatedTickets = group.LongCount() }).OrderBy(x => x.Date)
            .TagWith("Reports:daily").ToListAsync(cancellationToken);
        await FinishAsync(actor, signature, cancellationToken);
        var counts = rows.ToDictionary(x => x.Date, x => x.CreatedTickets);
        var days = new List<DailyTicketCount>();
        var lastDay = period.CreatedTo <= period.AsOf ? period.CreatedTo.AddTicks(-1).Date : period.AsOf.Date;
        // Bounded zero filling of aggregated days only, never of individual Tickets. End is exclusive.
        for (var day = period.CreatedFrom.Date; day <= lastDay; day = day.AddDays(1))
            days.Add(new(day, counts.GetValueOrDefault(day)));
        return new(period, days);
    }

    private async Task RequireReadAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw new AccountOperationException(AccountError.Unauthenticated);
        if (!await scope.HasReadGrantAsync(actor, cancellationToken)) throw new AccountOperationException(AccountError.InsufficientScope);
    }
    private async Task<string> StartAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, cancellationToken);
        return await scope.ReadAccessSignatureAsync(actor, cancellationToken);
    }
    private async Task FinishAsync(AccountActor actor, string signature, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, cancellationToken);
        if (signature != await scope.ReadAccessSignatureAsync(actor, cancellationToken)) throw new AccountOperationException(AccountError.VersionConflict);
    }
    private static TicketReportTotals Totals(ReportAggregate x) => new(x.Total, x.Open, x.Resolved, x.Closed, x.Cancelled, x.NoTeam, x.NoAgent);
    private static SlaReport Sla(ReportAggregate x) => new(x.WithSla, x.Total - x.WithSla, x.WithSla - x.Eligible, x.Eligible, x.BreachedTickets,
        Metric(x.Eligible, x.ResponseMet, x.ResponseBreached, x.ResponsePending, x.ResponseStopped, x.ResponseSamples, x.ResponseAverage),
        Metric(x.Eligible, x.ResolutionMet, x.ResolutionBreached, x.ResolutionPending, 0, x.ResolutionSamples, x.ResolutionAverage));
    private static SlaMetricReport Metric(long eligible, long met, long breached, long pending, long stopped, long samples, double? average) =>
        new(met, breached, pending, stopped, eligible - met - breached - pending - stopped, met + breached,
            met + breached == 0 ? null : Math.Round(100m * met / (met + breached), 2, MidpointRounding.AwayFromZero), samples,
            average is { } value ? Math.Round(value, 2, MidpointRounding.AwayFromZero) : null);
    private sealed record GroupName(Guid Id, string Name, bool IsActive);
}
