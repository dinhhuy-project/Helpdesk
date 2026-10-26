namespace be.Application.Features.Accounts;

public interface IAccountAdministrationStore
{
    Task<PagedResult<AccountUser>> ListAsync(AccountSearch search, CancellationToken cancellationToken);
    Task<AccountUser> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<AccountUser> CreateAsync(AccountActor actor, CreateAccount command, CancellationToken cancellationToken);
    Task<AccountUser> UpdateAsync(AccountActor actor, Guid id, UpdateAccount command, CancellationToken cancellationToken);
    Task<AccountUser> SetStatusAsync(AccountActor actor, Guid id, AccountStatus command, CancellationToken cancellationToken);
    Task<AccountUser> SetRolesAsync(AccountActor actor, Guid id, AccountRoles command, CancellationToken cancellationToken);
    Task ChangePasswordAsync(AccountActor actor, ChangeAccountPassword command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountRoleDetails>> RolesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountPermission>> PermissionsAsync(CancellationToken cancellationToken);
    Task<AccountRoleDetails> SetGrantsAsync(AccountActor actor, Guid id, RoleGrants command, CancellationToken cancellationToken);
    Task<PagedResult<AccountAudit>> AuditAsync(AccountActor actor, int page, int pageSize, CancellationToken cancellationToken);
}
