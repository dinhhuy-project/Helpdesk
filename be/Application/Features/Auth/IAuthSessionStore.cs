namespace be.Application.Features.Auth;

public interface IAuthSessionStore
{
    Task<RefreshSession?> CreateAsync(Guid userId, string expectedPasswordHash, CancellationToken cancellationToken);
    Task<RefreshSession?> RotateAsync(string refreshToken, CancellationToken cancellationToken);
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
    Task<bool> IsActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
}

// The raw value is returned once to the caller; persistence stores only its hash.
public sealed record RefreshSession(Guid UserId, Guid SessionId, string Value, DateTime ExpiresAt);
