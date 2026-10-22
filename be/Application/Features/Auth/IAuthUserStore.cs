using be.Domain.Entities;

namespace be.Application.Features.Auth;

public interface IAuthUserStore
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> UpdatePasswordHashAsync(Guid userId, string previousHash, string newHash, CancellationToken cancellationToken);
}
