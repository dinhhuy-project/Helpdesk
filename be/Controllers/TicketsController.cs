using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Contracts.Requests;
using be.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tickets")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class TicketsController(TicketReadService tickets, TicketCreateService creation, TicketMutationService mutations,
    TicketWorkflowReadService workflow) : ControllerBase
{
    [HttpPost]
    [RequirePermission(PermissionCodes.TicketCreate)]
    [Consumes("application/json")]
    [RequestSizeLimit(131072)]
    [ProducesResponseType<TicketCreated>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketCreated>> Create(CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var result = await creation.CreateAsync(CurrentAccountActor.From(User),
            new(request.Title, request.Description, request.CategoryId, request.Priority), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    // The three read grants combine with OR inside the resource scope, not multiple AND policies.
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<TicketSummary>> List([FromQuery] TicketSearchRequest request, CancellationToken cancellationToken) =>
        tickets.ListAsync(CurrentAccountActor.From(User), new(request.Page, request.PageSize, request.Search,
            request.Status, request.Priority, request.TeamId, request.CategoryId, request.TicketNumber, request.RequesterId,
            request.AgentId, request.Unassigned, request.CreatedFrom, request.CreatedTo, request.SortBy, request.Descending), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TicketDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TicketDetails> Get(Guid id, CancellationToken cancellationToken) => tickets.GetAsync(CurrentAccountActor.From(User), id, cancellationToken);

    [HttpGet("{id:guid}/access")]
    [ProducesResponseType<TicketAccess>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TicketAccess> Access(Guid id, CancellationToken cancellationToken) => tickets.AccessAsync(CurrentAccountActor.From(User), id, cancellationToken);

    [HttpGet("{id:guid}/workflow")]
    [ProducesResponseType<TicketWorkflowContext>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TicketWorkflowContext> Workflow(Guid id, CancellationToken cancellationToken) =>
        workflow.ContextAsync(CurrentAccountActor.From(User), id, cancellationToken);

    [HttpGet("{id:guid}/assignment-teams")]
    [RequirePermission(PermissionCodes.TicketAssign)]
    [ProducesResponseType<PagedResult<TicketAssignmentTeam>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<TicketAssignmentTeam>> AssignmentTeams(Guid id, [FromQuery] TicketAssignmentSearchRequest request, CancellationToken cancellationToken) =>
        workflow.TeamsAsync(CurrentAccountActor.From(User), id, new(request.Page, request.PageSize, request.Search), cancellationToken);

    [HttpGet("{id:guid}/assignment-teams/{teamId:guid}/agents")]
    [RequirePermission(PermissionCodes.TicketAssign)]
    [ProducesResponseType<PagedResult<TicketAssignmentAgent>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<TicketAssignmentAgent>> AssignmentAgents(Guid id, Guid teamId, [FromQuery] TicketAssignmentSearchRequest request, CancellationToken cancellationToken) =>
        workflow.AgentsAsync(CurrentAccountActor.From(User), id, teamId, new(request.Page, request.PageSize, request.Search), cancellationToken);

    [HttpPost("{id:guid}/assign")]
    [RequirePermission(PermissionCodes.TicketAssign)]
    [Consumes("application/json")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType<TicketMutationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TicketMutationResult> Assign(Guid id, AssignTicketRequest request, CancellationToken cancellationToken) =>
        mutations.AssignAsync(CurrentAccountActor.From(User), id, new(request.TeamId, request.AgentId, request.Version), cancellationToken);

    // The target selects the required operation/permission inside the locked transaction.
    [HttpPost("{id:guid}/transition")]
    [Consumes("application/json")]
    [RequestSizeLimit(32768)]
    [ProducesResponseType<TicketMutationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TicketMutationResult> Transition(Guid id, TransitionTicketRequest request, CancellationToken cancellationToken) =>
        mutations.TransitionAsync(CurrentAccountActor.From(User), id, new(request.Status, request.ResolutionSummary, request.Version), cancellationToken);
}
