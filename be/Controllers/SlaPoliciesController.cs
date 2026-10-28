using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Sla;
using be.Contracts.Requests;
using be.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Authorize]
[RequirePermission(PermissionCodes.TicketManageSla)]
[Route("api/v1/admin/sla-policies")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
public sealed class SlaPoliciesController(SlaService sla) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SlaPolicyDetails>> List([FromQuery] AccountPageRequest request, CancellationToken cancellationToken) =>
        sla.ListAsync(CurrentAccountActor.From(User), request.Page, request.PageSize, cancellationToken);
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SlaPolicyDetails> Get(Guid id, CancellationToken cancellationToken) => sla.GetPolicyAsync(CurrentAccountActor.From(User), id, cancellationToken);
    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<SlaPolicyDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SlaPolicyDetails>> Create(SaveSlaPolicyRequest request, CancellationToken cancellationToken)
    {
        var result = await sla.CreateAsync(CurrentAccountActor.From(User), new(request.Name, request.Priority, request.FirstResponseMinutes, request.ResolutionMinutes), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPatch("{id:guid}")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<SlaPolicyDetails> Update(Guid id, UpdateSlaPolicyRequest request, CancellationToken cancellationToken) =>
        sla.UpdateAsync(CurrentAccountActor.From(User), id, request.Version, new(request.Name, request.Priority, request.FirstResponseMinutes, request.ResolutionMinutes), cancellationToken);
    [HttpPut("{id:guid}/status")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<SlaPolicyDetails> Status(Guid id, SlaPolicyStatusRequest request, CancellationToken cancellationToken) =>
        sla.SetStatusAsync(CurrentAccountActor.From(User), id, request.Version, request.IsActive, cancellationToken);
}

[ApiController]
[Authorize]
[Route("api/v1/tickets/{id:guid}/sla")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TicketSlaController(SlaService sla) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TicketSlaDetails> Get(Guid id, CancellationToken cancellationToken) => sla.GetTicketAsync(CurrentAccountActor.From(User), id, cancellationToken);
}
