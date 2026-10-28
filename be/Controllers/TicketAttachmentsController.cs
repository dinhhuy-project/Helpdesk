using be.Api;
using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Contracts.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace be.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tickets/{ticketId:guid}/attachments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class TicketAttachmentsController(TicketAttachmentService attachments) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(ApiPolicies.AttachmentUpload)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024, ValueCountLimit = 8, ValueLengthLimit = 1024, KeyLengthLimit = 64)]
    [ProducesResponseType<TicketAttachmentDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TicketAttachmentDetails>> Upload(Guid ticketId, [FromForm] UploadTicketAttachmentRequest request, CancellationToken cancellationToken)
    {
        var form = Request.Form;
        if (form.Keys.Any(key => !new[] { "commentId", "isInternal" }.Contains(key, StringComparer.OrdinalIgnoreCase)) ||
            form.Any(field => field.Value.Count != 1) || form.Files.Count != 1 || !string.Equals(form.Files[0].Name, "file", StringComparison.OrdinalIgnoreCase))
            throw new TicketOperationException(TicketError.InvalidInput);
        await using var stream = request.File.OpenReadStream();
        var result = await attachments.UploadAsync(CurrentAccountActor.From(User), ticketId,
            new(request.File.FileName, request.File.ContentType, request.File.Length, request.CommentId, request.IsInternal), stream, cancellationToken);
        return CreatedAtAction(nameof(Get), new { ticketId, attachmentId = result.Id }, result);
    }
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketAttachmentDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<TicketAttachmentDetails>> List(Guid ticketId, [FromQuery] TicketAttachmentSearchRequest request, CancellationToken cancellationToken) =>
        attachments.ListAsync(CurrentAccountActor.From(User), ticketId, new(request.Page, request.PageSize, request.Descending, request.IsInternal), request.CommentId, cancellationToken);
    [HttpGet("{attachmentId:guid}")]
    [ProducesResponseType<TicketAttachmentDetails>(StatusCodes.Status200OK)]
    public Task<TicketAttachmentDetails> Get(Guid ticketId, Guid attachmentId, CancellationToken cancellationToken) =>
        attachments.GetAsync(CurrentAccountActor.From(User), ticketId, attachmentId, cancellationToken);
    [HttpGet("{attachmentId:guid}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Download(Guid ticketId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var result = await attachments.DownloadAsync(CurrentAccountActor.From(User), ticketId, attachmentId, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        return File(result.Content, result.Details.ContentType, result.Details.FileName, enableRangeProcessing: false);
    }
}
