using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Notifications;
using be.Contracts.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<NotificationDetails>> List([FromQuery] NotificationSearchRequest search, CancellationToken cancellationToken) =>
        notifications.ListAsync(CurrentAccountActor.From(User), new(search.Page, search.PageSize, search.Unread), cancellationToken);

    [HttpGet("unread-count")]
    public async Task<NotificationUnreadCount> UnreadCount(CancellationToken cancellationToken) =>
        new(await notifications.UnreadCountAsync(CurrentAccountActor.From(User), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<NotificationDetails> Get(Guid id, CancellationToken cancellationToken) => notifications.GetAsync(CurrentAccountActor.From(User), id, cancellationToken);

    [HttpPut("{id:guid}/read")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<NotificationDetails> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (Request.ContentLength > 0 || Request.Headers.TransferEncoding.Count > 0) throw new AccountOperationException(AccountError.InvalidInput);
        return notifications.MarkReadAsync(CurrentAccountActor.From(User), id, cancellationToken);
    }
}
