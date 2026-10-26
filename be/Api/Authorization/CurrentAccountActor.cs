using System.Security.Claims;
using be.Application.Features.Accounts;

namespace be.Api.Authorization;

internal static class CurrentAccountActor
{
    public static AccountActor From(ClaimsPrincipal principal)
    {
        if (Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId) && userId != Guid.Empty &&
            Guid.TryParse(principal.FindFirst("sid")?.Value, out var sessionId) && sessionId != Guid.Empty)
            return new(userId, sessionId);
        throw new AccountOperationException(AccountError.Unauthenticated);
    }
}
