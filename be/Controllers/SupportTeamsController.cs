using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.SupportTeams;
using be.Contracts.Requests;
using be.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Route("api/v1/support-teams")]
[RequirePermission(PermissionCodes.TeamManage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupportTeamsController(SupportTeamService teams) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SupportTeamDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<SupportTeamDetails>> List([FromQuery] SupportTeamSearchRequest request, CancellationToken cancellationToken) =>
        teams.ListAsync(CurrentAccountActor.From(User), new(request.Page, request.PageSize, request.Search,
            request.DepartmentId, request.IsActive, request.SortBy, request.Descending), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SupportTeamDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SupportTeamDetails> Get(Guid id, CancellationToken cancellationToken) =>
        teams.GetAsync(CurrentAccountActor.From(User), id, cancellationToken);

    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType<SupportTeamDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupportTeamDetails>> Create(CreateSupportTeamRequest request, CancellationToken cancellationToken)
    {
        var team = await teams.CreateAsync(CurrentAccountActor.From(User), new(request.DepartmentId, request.Name, request.Description), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = team.Id }, team);
    }

    [HttpPatch("{id:guid}")]
    [Consumes("application/json")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType<SupportTeamDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<SupportTeamDetails> Update(Guid id, UpdateSupportTeamRequest request, CancellationToken cancellationToken) =>
        teams.UpdateAsync(CurrentAccountActor.From(User), id, new(request.Name, request.Description, request.Version), cancellationToken);

    [HttpPut("{id:guid}/status")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<SupportTeamDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<SupportTeamDetails> SetStatus(Guid id, SupportTeamStatusRequest request, CancellationToken cancellationToken) =>
        teams.SetStatusAsync(CurrentAccountActor.From(User), id, new(request.IsActive, request.Version), cancellationToken);

    [HttpGet("{id:guid}/members")]
    [ProducesResponseType<PagedResult<TeamMemberDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<TeamMemberDetails>> Members(Guid id, [FromQuery] TeamMemberSearchRequest request, CancellationToken cancellationToken) =>
        teams.MembersAsync(CurrentAccountActor.From(User), id, new(request.Page, request.PageSize, request.IsActive), cancellationToken);

    [HttpPost("{id:guid}/members")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<TeamMembershipResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TeamMembershipResult>> AddMember(Guid id, AddTeamMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await teams.AddMemberAsync(CurrentAccountActor.From(User), id, new(request.UserId, request.TeamVersion), cancellationToken);
        return CreatedAtAction(nameof(Members), new { id }, result);
    }

    [HttpPut("{id:guid}/members/{userId:guid}/status")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<TeamMembershipResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TeamMembershipResult> SetMemberStatus(Guid id, Guid userId, TeamMemberStatusRequest request, CancellationToken cancellationToken) =>
        teams.SetMemberStatusAsync(CurrentAccountActor.From(User), id, userId,
            new(request.IsActive, request.Version, request.TeamVersion), cancellationToken);

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, [FromQuery] TeamMembershipVersionRequest request, CancellationToken cancellationToken)
    {
        await teams.SetMemberStatusAsync(CurrentAccountActor.From(User), id, userId,
            new(false, request.Version, request.TeamVersion), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/members/{userId:guid}/leadership")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<TeamMembershipResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TeamMembershipResult> SetLeadership(Guid id, Guid userId, TeamMemberLeadershipRequest request, CancellationToken cancellationToken) =>
        teams.SetLeadershipAsync(CurrentAccountActor.From(User), id, userId,
            new(request.IsLeader, request.Version, request.TeamVersion), cancellationToken);
}
