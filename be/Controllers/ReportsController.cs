using be.Api.Authorization;
using be.Application.Features.Reporting;
using be.Contracts.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("api/v1/dashboard")]
    public Task<DashboardReport> Dashboard([FromQuery] ReportFilterRequest request, CancellationToken cancellationToken) =>
        reports.DashboardAsync(CurrentAccountActor.From(User), Filter(request), cancellationToken);

    [HttpGet("api/v1/reports/tickets/teams")]
    public Task<GroupedTicketReport> Teams([FromQuery] GroupedReportRequest request, CancellationToken cancellationToken) =>
        reports.GroupsAsync(CurrentAccountActor.From(User), Filter(request), Page(request), false, cancellationToken);

    [HttpGet("api/v1/reports/tickets/agents")]
    public Task<GroupedTicketReport> Agents([FromQuery] GroupedReportRequest request, CancellationToken cancellationToken) =>
        reports.GroupsAsync(CurrentAccountActor.From(User), Filter(request), Page(request), true, cancellationToken);

    [HttpGet("api/v1/reports/tickets/daily")]
    public Task<DailyTicketReport> Daily([FromQuery] ReportFilterRequest request, CancellationToken cancellationToken) =>
        reports.DailyAsync(CurrentAccountActor.From(User), Filter(request), cancellationToken);

    private static ReportFilter Filter(ReportFilterRequest x) => new(x.CreatedFrom, x.CreatedTo, x.TeamId, x.AgentId, x.Status, x.Priority);
    private static ReportPage Page(GroupedReportRequest x) => new(x.Page, x.PageSize, x.SortBy, x.Descending);
}
