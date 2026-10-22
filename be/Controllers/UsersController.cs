using System.IdentityModel.Tokens.Jwt;
using be.Application.Features.Auth;
using be.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/users")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class UsersController(AuthService authentication) : ControllerBase
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
}
