namespace be.Application.Features.Auth;

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(Guid userId, Guid sessionId);
}

public sealed record IssuedAccessToken(string Value, DateTime ExpiresAt);
