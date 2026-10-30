using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Contracts.Requests;
using be.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[RequirePermission(PermissionCodes.RoleManage)]
[Route("api/v1/identity")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IdentityAdministrationController(AccountAdministrationService accounts) : ControllerBase
{
    [HttpGet("administration-access")]
    [ProducesResponseType<RoleAdministrationAccess>(StatusCodes.Status200OK)]
    public Task<RoleAdministrationAccess> Access(CancellationToken cancellationToken) =>
        accounts.RoleAccessAsync(CurrentAccountActor.From(User), cancellationToken);

    [HttpGet("assignment-users")]
    [ProducesResponseType<PagedResult<AccountUser>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AccountUser>> AssignmentUsers([FromQuery] AccountSearchRequest request, CancellationToken cancellationToken) =>
        accounts.AssignmentUsersAsync(CurrentAccountActor.From(User),
            new(request.Page, request.PageSize, request.Search, request.IsActive, request.SortBy, request.Descending), cancellationToken);

    [HttpGet("assignment-users/{id:guid}")]
    [ProducesResponseType<AccountUser>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AccountUser> AssignmentUser(Guid id, CancellationToken cancellationToken) =>
        accounts.AssignmentUserAsync(CurrentAccountActor.From(User), id, cancellationToken);

    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<AccountRoleDetails>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AccountRoleDetails>> Roles(CancellationToken cancellationToken) => accounts.RolesAsync(cancellationToken);

    [HttpGet("permissions")]
    [ProducesResponseType<IReadOnlyList<AccountPermission>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AccountPermission>> Permissions(CancellationToken cancellationToken) => accounts.PermissionsAsync(cancellationToken);

    [HttpPut("roles/{id:guid}/permissions")]
    [ProducesResponseType<AccountRoleDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    public Task<AccountRoleDetails> SetGrants(Guid id, RoleGrantsRequest request, CancellationToken cancellationToken) =>
        accounts.SetGrantsAsync(CurrentAccountActor.From(User), id, new(request.PermissionIds, request.Version), cancellationToken);

    [HttpGet("audit-logs")]
    [ProducesResponseType<PagedResult<AccountAudit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<AccountAudit>> Audit([FromQuery] AccountPageRequest request, CancellationToken cancellationToken) =>
        accounts.AuditAsync(CurrentAccountActor.From(User), request.Page, request.PageSize, cancellationToken);
}
