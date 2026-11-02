using be.Api;
using be.Application.Features.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace be.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting(ApiPolicies.CommunityPublic)]
[Route("api/v1/public/knowledge/retrieve")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class KnowledgeRetrievalController(KnowledgeRetrievalService retrieval) : ControllerBase
{
    [HttpGet]
    public Task<KnowledgeRetrievalResult> Search([FromQuery] KnowledgeRetrievalRequest request, CancellationToken ct) => retrieval.SearchAsync(request, ct);
}
