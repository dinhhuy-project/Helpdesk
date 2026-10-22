using System.IdentityModel.Tokens.Jwt;
using System.Text;
using be.Application.Features.Auth;
using be.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace be.Infrastructure.Authentication;

public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider) : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(Guid userId)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // JWT NumericDate has second precision; align the response expiration with the actual token.
        now = DateTimeOffset.FromUnixTimeSeconds(new DateTimeOffset(now).ToUnixTimeSeconds()).UtcDateTime;
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer, Audience = settings.Audience,
            IssuedAt = now, NotBefore = now, Expires = expiresAt, TokenType = "at+jwt",
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
            },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                settings.SigningKey ?? throw new InvalidOperationException("Jwt:SigningKey is required."))), SecurityAlgorithms.HmacSha256)
        };
        return new IssuedAccessToken(new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor), expiresAt);
    }
}
