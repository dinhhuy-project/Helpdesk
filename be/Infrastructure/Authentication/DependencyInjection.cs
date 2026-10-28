using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using be.Application.Features.Auth;
using be.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using be.Infrastructure.Realtime;

namespace be.Infrastructure.Authentication;

public static class DependencyInjection
{
    public static IServiceCollection AddHelpdeskAuthentication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DummyPasswordCredential>();
        services.AddScoped<IPasswordVerifier, PasswordVerifier>();
        services.AddScoped<IAuthUserStore, AuthUserStore>();
        services.AddScoped<IAuthSessionStore, AuthSessionStore>();
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<AuthService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, settings) =>
            {
                var jwt = settings.Value;
                bearer.MapInboundClaims = false;
                bearer.IncludeErrorDetails = false;
                bearer.SaveToken = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                        jwt.SigningKey ?? throw new InvalidOperationException("Jwt:SigningKey is required."))),
                    RequireSignedTokens = true, RequireExpirationTime = true, ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidTypes = ["at+jwt"],
                    NameClaimType = JwtRegisteredClaimNames.Sub
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Query-string tokens are accepted only on this hub, never on REST endpoints.
                        if (!context.Request.Headers.ContainsKey("Authorization") &&
                            context.Request.Path.StartsWithSegments(NotificationsHub.Path))
                        {
                            var token = context.Request.Query["access_token"];
                            if (token.Count == 1 && !string.IsNullOrWhiteSpace(token[0])) context.Token = token[0];
                        }
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        if (!Guid.TryParse(principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) ||
                            userId == Guid.Empty ||
                            !Guid.TryParse(principal?.FindFirst(JwtAccessTokenIssuer.SessionIdClaim)?.Value, out var sessionId) ||
                            sessionId == Guid.Empty ||
                            string.IsNullOrWhiteSpace(principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value) ||
                            !long.TryParse(principal?.FindFirst(JwtRegisteredClaimNames.Iat)?.Value,
                                NumberStyles.Integer, CultureInfo.InvariantCulture, out var issuedAt) || issuedAt <= 0)
                        {
                            context.Fail("Invalid access token.");
                            return;
                        }
                        var sessions = context.HttpContext.RequestServices.GetRequiredService<IAuthSessionStore>();
                        if (!await sessions.IsActiveAsync(userId, sessionId, context.HttpContext.RequestAborted))
                            context.Fail("Invalid access token.");
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        context.Response.Headers.CacheControl = "no-store";
                        await Results.Problem(statusCode: 401, title: "Yêu cầu xác thực.").ExecuteAsync(context.HttpContext);
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.Headers.CacheControl = "no-store";
                        await Results.Problem(statusCode: 403, title: "Bạn không có quyền thực hiện thao tác này.")
                            .ExecuteAsync(context.HttpContext);
                    }
                };
            });
        return services;
    }
}
