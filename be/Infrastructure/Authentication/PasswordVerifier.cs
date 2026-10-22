using System.Security.Cryptography;
using be.Application.Features.Auth;
using be.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace be.Infrastructure.Authentication;

// Constructed once, so unknown-email requests still perform a real password verification.
public sealed class DummyPasswordCredential
{
    public string Hash { get; }

    public DummyPasswordCredential(IOptions<PasswordHasherOptions> options)
    {
        var user = new User { Email = "unused@example.invalid", DisplayName = "Unused" };
        Hash = new PasswordHasher<User>(options).HashPassword(user, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }
}

public sealed class PasswordVerifier(IPasswordHasher<User> hasher, DummyPasswordCredential dummy) : IPasswordVerifier
{
    public PasswordCheck Verify(User? user, string password)
    {
        var candidate = user ?? new User { Email = "unused@example.invalid", DisplayName = "Unused" };
        var hash = user?.PasswordHash;
        PasswordVerificationResult result;
        try
        {
            result = hasher.VerifyHashedPassword(candidate, string.IsNullOrEmpty(hash) ? dummy.Hash : hash, password);
        }
        catch (FormatException)
        {
            // Malformed stored hashes do not turn authentication failures into account-specific server errors.
            hasher.VerifyHashedPassword(candidate, dummy.Hash, password);
            return new PasswordCheck(false);
        }
        if (string.IsNullOrEmpty(hash) || result == PasswordVerificationResult.Failed) return new PasswordCheck(false);
        return new PasswordCheck(true, result == PasswordVerificationResult.SuccessRehashNeeded && candidate.IsActive
            ? hasher.HashPassword(candidate, password) : null);
    }
}
