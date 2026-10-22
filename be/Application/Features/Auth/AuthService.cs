namespace be.Application.Features.Auth;

public sealed class AuthService(IAuthUserStore users, IPasswordVerifier passwords, IAccessTokenIssuer tokens)
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
        }

        var profile = await users.GetProfileAsync(user.Id, cancellationToken);
        if (profile is null) return null;
        return new LoginResult(tokens.Issue(user.Id), profile);
    }

    public Task<UserProfile?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) =>
        users.GetProfileAsync(userId, cancellationToken);
}

public sealed record LoginResult(IssuedAccessToken Token, UserProfile User);
