using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Contracts.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tickets")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class TicketsController(TicketReadService tickets) : ControllerBase
{
    // The three read grants combine with OR inside the resource scope, not multiple AND policies.
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<TicketSummary>> List([FromQuery] TicketSearchRequest request, CancellationToken cancellationToken) =>
        tickets.ListAsync(CurrentAccountActor.From(User), new(request.Page, request.PageSize, request.Search,
            request.Status, request.Priority, request.TeamId, request.CategoryId), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TicketDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TicketDetails> Get(Guid id, CancellationToken cancellationToken) => tickets.GetAsync(CurrentAccountActor.From(User), id, cancellationToken);

    [HttpGet("{id:guid}/access")]
    [ProducesResponseType<TicketAccess>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TicketAccess> Access(Guid id, CancellationToken cancellationToken) => tickets.AccessAsync(CurrentAccountActor.From(User), id, cancellationToken);
}
