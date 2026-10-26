namespace be.Application.Features.Auth;

public sealed class AuthService(
    IAuthUserStore users, IPasswordVerifier passwords, IAccessTokenIssuer tokens, IAuthSessionStore sessions)
{
    public async Task<LoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        // Verify even absent/passwordless/inactive accounts; the verifier uses a dummy hash when needed.
        var check = passwords.Verify(user, password);
        if (!check.Succeeded || user is null || !user.IsActive) return null;
        if (check.ReplacementHash is not null)
        {
            if (user.PasswordHash is null || !await users.UpdatePasswordHashAsync(
                    user.Id, user.PasswordHash, check.ReplacementHash, cancellationToken))
                return null;
            user.PasswordHash = check.ReplacementHash;
        }

        var profile = await users.GetProfileAsync(user.Id, cancellationToken);
        if (profile is null) return null;
        if (user.PasswordHash is null) return null;
        var session = await sessions.CreateAsync(user.Id, user.PasswordHash, cancellationToken);
        return session is null ? null : CreateResult(session, profile);
    }

    public async Task<LoginResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = await sessions.RotateAsync(refreshToken, cancellationToken);
        if (session is null) return null;
        var profile = await users.GetProfileAsync(session.UserId, cancellationToken);
        if (profile is null)
        {
            await sessions.RevokeAsync(session.Value, cancellationToken);
            return null;
        }
        return CreateResult(session, profile);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken) =>
        sessions.RevokeAsync(refreshToken, cancellationToken);

    private LoginResult CreateResult(RefreshSession session, UserProfile profile) =>
        new(tokens.Issue(session.UserId, session.SessionId), profile, session.Value, session.ExpiresAt);

    public Task<UserProfile?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) =>
        users.GetProfileAsync(userId, cancellationToken);
}

public sealed record LoginResult(
    IssuedAccessToken Token, UserProfile User, string RefreshToken, DateTime RefreshTokenExpiresAt);
