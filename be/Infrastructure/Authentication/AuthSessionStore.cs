using System.Security.Cryptography;
using System.Text;
using be.Application.Features.Auth;
using be.Configuration;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace be.Infrastructure.Authentication;

public sealed class AuthSessionStore(
    HelpdeskDbContext database, IOptions<JwtOptions> options, TimeProvider timeProvider) : IAuthSessionStore
{
    public async Task<RefreshSession?> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        if (user is null || !user.IsActive) return null;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (token, value) = CreateToken(userId, now, now.AddDays(options.Value.RefreshTokenDays));
        database.RefreshTokens.Add(token);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefreshSession(userId, token.Id, value, token.ExpiresAt);
    }

    public async Task<RefreshSession?> RotateAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var owner = await FindOwnerAsync(refreshToken, cancellationToken);
        if (owner is null) return null;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // All session mutations lock the same user row, across API instances. Read token state AFTER locking.
        var user = await LockUserAsync(owner.UserId, cancellationToken);
        var current = await database.RefreshTokens.SingleAsync(x => x.Id == owner.Id, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (user is null || !user.IsActive || current.RevokedAt is not null || current.ExpiresAt <= now)
        {
            // A replay must commit the revocation even though the HTTP result will be 401.
            await RevokeChainAsync(owner.Id, owner.UserId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        // Absolute lifetime: rotation never extends the original login's refresh expiration.
        var (replacement, value) = CreateToken(current.UserId, now, current.ExpiresAt);
        database.RefreshTokens.Add(replacement);
        current.RevokedAt = now;
        current.ReplacedByTokenId = replacement.Id;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefreshSession(current.UserId, replacement.Id, value, replacement.ExpiresAt);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var owner = await FindOwnerAsync(refreshToken, cancellationToken);
        if (owner is null) return;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await LockUserAsync(owner.UserId, cancellationToken);
        // Accept even an old token from this chain, so logout racing with refresh still revokes the new pair.
        await RevokeChainAsync(owner.Id, owner.UserId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<bool> IsActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return database.RefreshTokens.AnyAsync(x => x.Id == sessionId && x.UserId == userId &&
            x.RevokedAt == null && x.ExpiresAt > now &&
            database.Users.Any(user => user.Id == userId && user.IsActive), cancellationToken);
    }

    private Task<User?> LockUserAsync(Guid userId, CancellationToken cancellationToken) =>
        database.Users.FromSqlInterpolated($"SELECT * FROM identity.users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    private async Task<TokenOwner?> FindOwnerAsync(string value, CancellationToken cancellationToken)
    {
        // A 64-byte base64url credential has exactly 86 characters. Never trim or log credentials.
        if (value.Length != 86 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return null;
        var hash = Hash(value);
        return await database.RefreshTokens.AsNoTracking().Where(x => x.TokenHash == hash)
            .Select(x => new TokenOwner(x.Id, x.UserId)).SingleOrDefaultAsync(cancellationToken);
    }

    private async Task RevokeChainAsync(Guid tokenId, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        // UNION also bounds traversal if old/corrupt data contains a cycle. Never cross user boundaries.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            WITH RECURSIVE chain AS (
                SELECT id, replaced_by_token_id FROM identity.refresh_tokens
                WHERE id = {tokenId} AND user_id = {userId}
                UNION
                SELECT next.id, next.replaced_by_token_id FROM identity.refresh_tokens AS next
                JOIN chain ON next.id = chain.replaced_by_token_id
                WHERE next.user_id = {userId}
            )
            UPDATE identity.refresh_tokens SET revoked_at = COALESCE(revoked_at, {now})
            WHERE id IN (SELECT id FROM chain) AND user_id = {userId}
            """, cancellationToken);
    }

    private static (RefreshToken Token, string Value) CreateToken(Guid userId, DateTime now, DateTime expiresAt)
    {
        var value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        return (new RefreshToken { UserId = userId, TokenHash = Hash(value), CreatedAt = now, ExpiresAt = expiresAt }, value);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record TokenOwner(Guid Id, Guid UserId);
}
