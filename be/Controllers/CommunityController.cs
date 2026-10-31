using be.Api;
using be.Api.Authorization;
using be.Application.Features.Community;
using be.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace be.Controllers;

public sealed record CommunityVersionRequest(uint Version);
public sealed record CommunityParticipationRequest(bool Value, bool Solved = false);

[ApiController]
[AllowAnonymous]
[EnableRateLimiting(ApiPolicies.CommunityPublic)]
[Route("api/v1/public/{kind}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicCommunityController(CommunityService community) : ControllerBase
{
    [HttpGet]
    public Task<be.Application.Features.Accounts.PagedResult<PublicCommunityEntry>> List(string kind, [FromQuery] CommunitySearch request, CancellationToken ct) =>
        community.PublicListAsync(CommunityService.Kind(kind), request, ct);
    [HttpGet("{id:guid}")]
    public Task<PublicCommunityEntry> Get(string kind, Guid id, CancellationToken ct) => community.PublicGetAsync(CommunityService.Kind(kind), id, ct);
    [HttpGet("{id:guid}/updates")]
    public Task<be.Application.Features.Accounts.PagedResult<PublicIncidentUpdate>> Updates(string kind, Guid id, [FromQuery] CommunitySearch request, CancellationToken ct)
    {
        if (CommunityService.Kind(kind) != be.Domain.Entities.CommunityKind.Incident) throw new CommunityOperationException(CommunityError.NotFound);
        return community.UpdatesAsync(id, request, ct);
    }
}

[ApiController]
[Authorize]
[Route("api/v1/community/{kind}")]
[RequestSizeLimit(131072)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CommunityController(CommunityService community) : ControllerBase
{
    [HttpGet]
    public Task<be.Application.Features.Accounts.PagedResult<ManagedCommunityEntry>> List(string kind, [FromQuery] CommunitySearch request, CancellationToken ct) =>
        community.ManageListAsync(CurrentAccountActor.From(User), CommunityService.Kind(kind), request, ct);
    [HttpGet("{id:guid}")]
    public Task<ManagedCommunityEntry> Get(string kind, Guid id, CancellationToken ct) => community.ManageGetAsync(CurrentAccountActor.From(User), CommunityService.Kind(kind), id, ct);
    [HttpPost]
    public async Task<ActionResult<ManagedCommunityEntry>> Create(string kind, SaveCommunityEntry request, CancellationToken ct)
    {
        var row = await community.SaveAsync(CurrentAccountActor.From(User), CommunityService.Kind(kind), null, request, ct);
        return CreatedAtAction(nameof(Get), new { kind, id = row.Id }, row);
    }
    [HttpPut("{id:guid}")]
    public Task<ManagedCommunityEntry> Save(string kind, Guid id, SaveCommunityEntry request, CancellationToken ct) =>
        community.SaveAsync(CurrentAccountActor.From(User), CommunityService.Kind(kind), id, request, ct);
    [HttpPost("{id:guid}/publish")]
    public Task<ManagedCommunityEntry> Publish(string kind, Guid id, PublishCommunityEntry request, CancellationToken ct) =>
        community.PublishAsync(CurrentAccountActor.From(User), CommunityService.Kind(kind), id, request, ct);
    [HttpPost("{id:guid}/withdraw")]
    public Task<ManagedCommunityEntry> Withdraw(string kind, Guid id, CommunityVersionRequest request, CancellationToken ct) =>
        community.WithdrawAsync(CurrentAccountActor.From(User), CommunityService.Kind(kind), id, request.Version, ct);
    [HttpGet("{id:guid}/participation")]
    public Task<CommunityParticipation> Participation(string kind, Guid id, CancellationToken ct) =>
        community.ParticipationAsync(CurrentAccountActor.From(User), CommunityService.Kind(kind), id, ct);
    [HttpPut("{id:guid}/participation")]
    [EnableRateLimiting(ApiPolicies.CommunityPublic)]
    public Task<CommunityParticipation> Participate(string kind, Guid id, CommunityParticipationRequest request, CancellationToken ct) =>
        community.ParticipateAsync(CurrentAccountActor.From(User), CommunityService.Kind(kind), id, request.Value, request.Solved, ct);
    [HttpPost("from-ticket/{ticketId:guid}")]
    [RequirePermission(PermissionCodes.KnowledgeManage)]
    public Task<ManagedCommunityEntry> FromTicket(string kind, Guid ticketId, CancellationToken ct)
    {
        if (CommunityService.Kind(kind) != be.Domain.Entities.CommunityKind.Knowledge) throw new CommunityOperationException(CommunityError.InvalidInput);
        return community.FromTicketAsync(CurrentAccountActor.From(User), ticketId, ct);
    }
}
