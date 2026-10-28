using System.Linq.Expressions;
using be.Application.Features.Accounts;
using be.Application.Features.Reporting;
using be.Application.Features.Tickets;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using be.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Reporting;

internal enum SlaOutcome { Unknown, Met, Breached, Pending, Stopped }

// Infrastructure-only SQL projections. No Ticket collection is materialized to aggregate in memory.
internal sealed class ReportTicket
{
    public Guid? TeamId { get; init; }
    public Guid? AgentId { get; init; }
    public DateTime CreatedAt { get; init; }
    public TicketStatus Status { get; init; }
    public TicketPriority Priority { get; init; }
    public bool HasSla { get; init; }
    public bool Eligible { get; init; }
    public SlaOutcome Response { get; init; }
    public SlaOutcome Resolution { get; init; }
    public double? ResponseMinutes { get; init; }
    public double? ResolutionMinutes { get; init; }
}

internal sealed class ReportAggregate
{
    public Guid? Id { get; init; }
    public long Total { get; init; }
    public long New { get; init; }
    public long InProgress { get; init; }
    public long Waiting { get; init; }
    public long Resolved { get; init; }
    public long Closed { get; init; }
    public long Cancelled { get; init; }
    public long Reopened { get; init; }
    public long Open { get; init; }
    public long Low { get; init; }
    public long Normal { get; init; }
    public long High { get; init; }
    public long Urgent { get; init; }
    public long NoTeam { get; init; }
    public long NoAgent { get; init; }
    public long WithSla { get; init; }
    public long Eligible { get; init; }
    public long BreachedTickets { get; init; }
    public long ResponseMet { get; init; }
    public long ResponseBreached { get; init; }
    public long ResponsePending { get; init; }
    public long ResponseStopped { get; init; }
    public long ResolutionMet { get; init; }
    public long ResolutionBreached { get; init; }
    public long ResolutionPending { get; init; }
    public long ResponseSamples { get; init; }
    public long ResolutionSamples { get; init; }
    public double? ResponseAverage { get; init; }
    public double? ResolutionAverage { get; init; }
}

internal static class ReportQueries
{
    internal static IQueryable<ReportTicket> Tickets(HelpdeskDbContext database, TicketScope scope, AccountActor actor, ReportFilter filter, ReportPeriod period)
    {
        var query = scope.For(actor, TicketOperation.Read).AsNoTracking().Where(x => x.CreatedAt >= period.CreatedFrom &&
            x.CreatedAt < period.CreatedTo && x.CreatedAt <= period.AsOf);
        if (filter.TeamId is not null) query = query.Where(x => x.AssignedTeamId == filter.TeamId);
        if (filter.AgentId is not null) query = query.Where(x => x.AssignedAgentId == filter.AgentId);
        if (filter.Status is not null) query = query.Where(x => x.Status == filter.Status);
        if (filter.Priority is not null) query = query.Where(x => x.Priority == filter.Priority);

        var facts = from ticket in query
                    join snapshot in database.TicketSlas.AsNoTracking() on ticket.Id equals snapshot.TicketId into snapshots
                    from sla in snapshots.DefaultIfEmpty()
                    select new
                    {
                        ticket.AssignedTeamId, ticket.AssignedAgentId, ticket.CreatedAt, ticket.Status, ticket.Priority,
                        HasSla = sla != null, Eligible = sla != null && ticket.Status != TicketStatus.Cancelled,
                        Applied = sla == null ? (DateTime?)null : sla.AppliedAt,
                        ResponseAt = sla == null ? null : sla.FirstRespondedAt, ResolvedAt = sla == null ? null : sla.ResolvedAt,
                        ResponseDue = sla == null ? (DateTime?)null : sla.FirstResponseDueAt,
                        ResolutionDue = sla == null ? (DateTime?)null : sla.ResolutionDueAt,
                        // Mirrors SlaRules; integration parity tests cover every status and completion boundary.
                        ResponseBreached = sla != null && (sla.FirstResponseBreachedAt != null ||
                            (ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled &&
                             sla.FirstResponseDueAt < (sla.FirstRespondedAt != null && sla.FirstRespondedAt < (sla.ResolvedAt ?? period.AsOf)
                                ? sla.FirstRespondedAt.Value : sla.ResolvedAt ?? period.AsOf))),
                        ResolutionBreached = sla != null && (sla.ResolutionBreachedAt != null ||
                            (ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled && sla.ResolutionDueAt < (sla.ResolvedAt ?? period.AsOf)))
                    };
        return facts.Select(x => new ReportTicket
        {
            TeamId = x.AssignedTeamId, AgentId = x.AssignedAgentId, CreatedAt = x.CreatedAt, Status = x.Status, Priority = x.Priority,
            HasSla = x.HasSla, Eligible = x.Eligible,
            Response = !x.Eligible ? SlaOutcome.Unknown : x.ResponseBreached ? SlaOutcome.Breached :
                x.ResponseAt != null && x.ResponseAt >= x.Applied && x.ResponseAt <= x.ResponseDue ? SlaOutcome.Met :
                x.ResponseAt == null && x.ResolvedAt != null ? SlaOutcome.Stopped :
                x.ResponseAt == null && x.ResolvedAt == null && x.Status != TicketStatus.Closed && x.Status != TicketStatus.Resolved ? SlaOutcome.Pending : SlaOutcome.Unknown,
            Resolution = !x.Eligible ? SlaOutcome.Unknown : x.ResolutionBreached ? SlaOutcome.Breached :
                x.ResolvedAt != null && x.ResolvedAt >= x.Applied && x.ResolvedAt <= x.ResolutionDue ? SlaOutcome.Met :
                x.ResolvedAt == null && x.Status != TicketStatus.Closed && x.Status != TicketStatus.Resolved ? SlaOutcome.Pending : SlaOutcome.Unknown,
            ResponseMinutes = x.Eligible && x.ResponseAt >= x.Applied ? (x.ResponseAt!.Value - x.Applied!.Value).TotalMinutes : null,
            ResolutionMinutes = x.Eligible && x.ResolvedAt >= x.Applied ? (x.ResolvedAt!.Value - x.Applied!.Value).TotalMinutes : null
        });
    }

    internal static readonly Expression<Func<IGrouping<Guid?, ReportTicket>, ReportAggregate>> Aggregate = group => new ReportAggregate
    {
        Id = group.Key, Total = group.LongCount(),
        New = group.LongCount(x => x.Status == TicketStatus.New), InProgress = group.LongCount(x => x.Status == TicketStatus.InProgress),
        Waiting = group.LongCount(x => x.Status == TicketStatus.WaitingForRequester), Resolved = group.LongCount(x => x.Status == TicketStatus.Resolved),
        Closed = group.LongCount(x => x.Status == TicketStatus.Closed), Cancelled = group.LongCount(x => x.Status == TicketStatus.Cancelled),
        Reopened = group.LongCount(x => x.Status == TicketStatus.Reopened),
        Open = group.LongCount(x => x.Status != TicketStatus.Resolved && x.Status != TicketStatus.Closed && x.Status != TicketStatus.Cancelled),
        Low = group.LongCount(x => x.Priority == TicketPriority.Low), Normal = group.LongCount(x => x.Priority == TicketPriority.Normal),
        High = group.LongCount(x => x.Priority == TicketPriority.High), Urgent = group.LongCount(x => x.Priority == TicketPriority.Urgent),
        NoTeam = group.LongCount(x => x.TeamId == null), NoAgent = group.LongCount(x => x.AgentId == null),
        WithSla = group.LongCount(x => x.HasSla), Eligible = group.LongCount(x => x.Eligible),
        BreachedTickets = group.LongCount(x => x.Eligible && (x.Response == SlaOutcome.Breached || x.Resolution == SlaOutcome.Breached)),
        ResponseMet = group.LongCount(x => x.Eligible && x.Response == SlaOutcome.Met), ResponseBreached = group.LongCount(x => x.Eligible && x.Response == SlaOutcome.Breached),
        ResponsePending = group.LongCount(x => x.Eligible && x.Response == SlaOutcome.Pending), ResponseStopped = group.LongCount(x => x.Eligible && x.Response == SlaOutcome.Stopped),
        ResolutionMet = group.LongCount(x => x.Eligible && x.Resolution == SlaOutcome.Met), ResolutionBreached = group.LongCount(x => x.Eligible && x.Resolution == SlaOutcome.Breached),
        ResolutionPending = group.LongCount(x => x.Eligible && x.Resolution == SlaOutcome.Pending),
        ResponseSamples = group.LongCount(x => x.ResponseMinutes != null), ResolutionSamples = group.LongCount(x => x.ResolutionMinutes != null),
        ResponseAverage = group.Average(x => x.ResponseMinutes), ResolutionAverage = group.Average(x => x.ResolutionMinutes)
    };
}
