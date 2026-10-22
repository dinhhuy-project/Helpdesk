using be.Domain.Entities;

namespace be.Application.Features.Auth;

public interface IPasswordVerifier
{
    PasswordCheck Verify(User? user, string password);
}

public sealed record PasswordCheck(bool Succeeded, string? ReplacementHash = null);
