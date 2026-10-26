using System.IdentityModel.Tokens.Jwt;
using be.Application.Features.Auth;
using be.Application.Features.Accounts;
using be.Api;
using be.Api.Authorization;
using be.Contracts.Requests;
using be.Domain.Constants;
using be.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace be.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/users")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class UsersController(AuthService authentication, AccountAdministrationService accounts) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)) return Challenge();
        var profile = await authentication.GetCurrentUserAsync(userId, cancellationToken);
        if (profile is null) return Challenge();
        return Ok(UserResponse.FromProfile(profile));
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<AccountUser>>(StatusCodes.Status200OK)]
    [RequirePermission(PermissionCodes.UserManage)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<AccountUser>> List([FromQuery] AccountSearchRequest request, CancellationToken cancellationToken) =>
        accounts.ListAsync(new(request.Page, request.PageSize, request.Search, request.IsActive, request.SortBy, request.Descending), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AccountUser>(StatusCodes.Status200OK)]
    [RequirePermission(PermissionCodes.UserManage)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AccountUser> Get(Guid id, CancellationToken cancellationToken) => accounts.GetAsync(id, cancellationToken);

    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [RequirePermission(PermissionCodes.UserManage)]
    [ProducesResponseType<AccountUser>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountUser>> Create(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var user = await accounts.CreateAsync(CurrentAccountActor.From(User), new(request.Email, request.DisplayName, request.Password), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<AccountUser>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [Consumes("application/json")]
    [RequirePermission(PermissionCodes.UserManage)]
    [RequestSizeLimit(8192)]
    public Task<AccountUser> Update(Guid id, UpdateAccountRequest request, CancellationToken cancellationToken) =>
        accounts.UpdateAsync(CurrentAccountActor.From(User), id, new(request.DisplayName, request.DepartmentId, request.Version), cancellationToken);

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType<AccountUser>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [Consumes("application/json")]
    [RequirePermission(PermissionCodes.UserManage)]
    [RequestSizeLimit(4096)]
    public Task<AccountUser> SetStatus(Guid id, AccountStatusRequest request, CancellationToken cancellationToken) =>
        accounts.SetStatusAsync(CurrentAccountActor.From(User), id, new(request.IsActive, request.Version), cancellationToken);

    [HttpPut("{id:guid}/roles")]
    [ProducesResponseType<AccountUser>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [Consumes("application/json")]
    [RequirePermission(PermissionCodes.RoleManage)]
    [RequestSizeLimit(8192)]
    public Task<AccountUser> SetRoles(Guid id, AccountRolesRequest request, CancellationToken cancellationToken) =>
        accounts.SetRolesAsync(CurrentAccountActor.From(User), id, new(request.RoleIds, request.Version), cancellationToken);

    [HttpPost("me/password")]
    [Consumes("application/json")]
    [EnableRateLimiting(ApiPolicies.AuthenticationRateLimit)]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword(ChangeAccountPasswordRequest request, CancellationToken cancellationToken)
    {
        await accounts.ChangePasswordAsync(CurrentAccountActor.From(User), new(request.CurrentPassword, request.NewPassword), cancellationToken);
        return NoContent();
    }
}
