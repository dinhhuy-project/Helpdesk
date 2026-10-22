namespace be.Application.Common.Authorization;

public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(Guid userId, Guid sessionId, string permission, CancellationToken cancellationToken);
}
