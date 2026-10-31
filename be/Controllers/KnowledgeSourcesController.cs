using be.Api;
using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Community;
using be.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace be.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/public/knowledge-sources")]
[EnableRateLimiting(ApiPolicies.CommunityPublic)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicKnowledgeSourcesController(KnowledgeSourceService sources) : ControllerBase
{
    [HttpGet]
    public KnowledgeSourceStatus Status() => sources.Status;
    [HttpGet("wikipedia")]
    public Task<WikipediaSearchResult> Wikipedia([FromQuery] string search, [FromQuery] int offset = 0, CancellationToken ct = default) => sources.WikipediaAsync(search, offset, ct);
}

public sealed record BookStackImportRequest(int PageId);

[ApiController]
[Authorize]
[RequirePermission(PermissionCodes.KnowledgeManage)]
[Route("api/v1/community/knowledge-sources/bookstack")]
[EnableRateLimiting(ApiPolicies.CommunityPublic)]
[RequestSizeLimit(4096)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class KnowledgeSourcesController(KnowledgeSourceService sources) : ControllerBase
{
    [HttpGet("pages")]
    public Task<PagedResult<BookStackPage>> Pages([FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default) =>
        sources.PagesAsync(CurrentAccountActor.From(User), search, page, ct);
    [HttpGet("pages/{id:int}")]
    public Task<BookStackPreview> Preview(int id, CancellationToken ct) => sources.PreviewAsync(CurrentAccountActor.From(User), id, ct);
    [HttpPost("imports")]
    public Task<KnowledgeImportResult> Import(BookStackImportRequest request, CancellationToken ct) => sources.ImportAsync(CurrentAccountActor.From(User), request.PageId, ct);
}
