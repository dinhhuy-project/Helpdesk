using be.Api;
using be.Application.Features.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace be.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting(ApiPolicies.KnowledgeAssistant)]
[Route("api/v1/public/assistant")]
[RequestSizeLimit(4096)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class KnowledgeAssistantController(KnowledgeAssistantService assistant) : ControllerBase
{
    [HttpPost]
    public Task<KnowledgeAssistantResponse> Ask(AskKnowledgeAssistant request, CancellationToken ct) => assistant.AskAsync(request, ct);
}
