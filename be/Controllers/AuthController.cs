using be.Api;
using be.Application.Features.Auth;
using be.Contracts.Requests;
using be.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace be.Controllers;

[ApiController]
[Route("api/v1/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController(AuthService authentication) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting(ApiPolicies.AuthenticationRateLimit)]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.LoginAsync(request.Email, request.Password, cancellationToken);
        if (result is null)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Email hoặc mật khẩu không hợp lệ.");
        return Ok(new LoginResponse(result.Token.Value, "Bearer", result.Token.ExpiresAt, UserResponse.FromProfile(result.User)));
    }
}
