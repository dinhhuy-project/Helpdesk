using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Contracts.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tickets/{ticketId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class TicketDiscussionController(TicketDiscussionService discussion) : ControllerBase
{
    [HttpGet("discussion-access")]
    [ProducesResponseType<TicketDiscussionAccess>(StatusCodes.Status200OK)]
    public Task<TicketDiscussionAccess> Access(Guid ticketId, CancellationToken cancellationToken) =>
        discussion.AccessAsync(CurrentAccountActor.From(User), ticketId, cancellationToken);

    // Visibility determines the required permission inside the transaction; public/internal
    // are alternatives, not two AND policies. Author identity never comes from the request.
    [HttpPost("comments")]
    [Consumes("application/json")]
    [RequestSizeLimit(131072)]
    [ProducesResponseType<TicketCommentDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketCommentDetails>> Create(Guid ticketId, CreateTicketCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await discussion.CreateAsync(CurrentAccountActor.From(User), ticketId, new(request.Content, request.IsInternal), cancellationToken);
        return CreatedAtAction(nameof(GetComment), new { ticketId, commentId = result.Id }, result);
    }
    [HttpGet("comments")]
    [ProducesResponseType<PagedResult<TicketCommentDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<TicketCommentDetails>> ListComments(Guid ticketId, [FromQuery] TicketDiscussionSearchRequest search, CancellationToken cancellationToken) =>
        discussion.ListCommentsAsync(CurrentAccountActor.From(User), ticketId, new(search.Page, search.PageSize, search.Descending, search.IsInternal), cancellationToken);
    [HttpGet("comments/{commentId:guid}")]
    [ProducesResponseType<TicketCommentDetails>(StatusCodes.Status200OK)]
    public Task<TicketCommentDetails> GetComment(Guid ticketId, Guid commentId, CancellationToken cancellationToken) =>
        discussion.GetCommentAsync(CurrentAccountActor.From(User), ticketId, commentId, cancellationToken);
    [HttpGet("activities")]
    [ProducesResponseType<PagedResult<TicketActivityDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<TicketActivityDetails>> ListActivities(Guid ticketId, [FromQuery] TicketDiscussionSearchRequest search, CancellationToken cancellationToken) =>
        discussion.ListActivitiesAsync(CurrentAccountActor.From(User), ticketId, new(search.Page, search.PageSize, search.Descending, search.IsInternal), cancellationToken);
    [HttpGet("activities/{activityId:guid}")]
    [ProducesResponseType<TicketActivityDetails>(StatusCodes.Status200OK)]
    public Task<TicketActivityDetails> GetActivity(Guid ticketId, Guid activityId, CancellationToken cancellationToken) =>
        discussion.GetActivityAsync(CurrentAccountActor.From(User), ticketId, activityId, cancellationToken);
}
