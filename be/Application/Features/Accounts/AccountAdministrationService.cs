namespace be.Application.Features.Accounts;

public sealed class AccountAdministrationService(IAccountAdministrationStore store)
{
    public Task<PagedResult<AccountUser>> ListAsync(AccountSearch search, CancellationToken cancellationToken) =>
        store.ListAsync(search with { Search = search.Search?.Trim().ToLowerInvariant() }, cancellationToken);
    public Task<AccountUser> GetAsync(Guid id, CancellationToken cancellationToken) => store.GetAsync(id, cancellationToken);
    public Task<AccountAdministrationAccess> AccessAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) =>
        store.AccessAsync(actor, id, cancellationToken);
    public Task<RoleAdministrationAccess> RoleAccessAsync(AccountActor actor, CancellationToken cancellationToken) =>
        store.RoleAccessAsync(actor, cancellationToken);
    public Task<PagedResult<AccountUser>> AssignmentUsersAsync(AccountActor actor, AccountSearch search, CancellationToken cancellationToken) =>
        store.AssignmentUsersAsync(actor, search with { Search = search.Search?.Trim().ToLowerInvariant() }, cancellationToken);
    public Task<AccountUser> AssignmentUserAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) =>
        store.AssignmentUserAsync(actor, id, cancellationToken);
    public Task<AccountUser> CreateAsync(AccountActor actor, CreateAccount command, CancellationToken cancellationToken) =>
        store.CreateAsync(actor, command with { Email = command.Email.Trim().ToLowerInvariant(), DisplayName = command.DisplayName.Trim() }, cancellationToken);
    public Task<AccountUser> UpdateAsync(AccountActor actor, Guid id, UpdateAccount command, CancellationToken cancellationToken) =>
        store.UpdateAsync(actor, id, command with { DisplayName = command.DisplayName.Trim() }, cancellationToken);
    public Task<AccountUser> SetStatusAsync(AccountActor actor, Guid id, AccountStatus command, CancellationToken cancellationToken) =>
        store.SetStatusAsync(actor, id, command, cancellationToken);
    public Task<AccountUser> SetRolesAsync(AccountActor actor, Guid id, AccountRoles command, CancellationToken cancellationToken) =>
        store.SetRolesAsync(actor, id, command, cancellationToken);
    public Task ChangePasswordAsync(AccountActor actor, ChangeAccountPassword command, CancellationToken cancellationToken) =>
        store.ChangePasswordAsync(actor, command, cancellationToken);
    public Task<IReadOnlyList<AccountRoleDetails>> RolesAsync(CancellationToken cancellationToken) => store.RolesAsync(cancellationToken);
    public Task<IReadOnlyList<AccountPermission>> PermissionsAsync(CancellationToken cancellationToken) => store.PermissionsAsync(cancellationToken);
    public Task<AccountRoleDetails> SetGrantsAsync(AccountActor actor, Guid id, RoleGrants command, CancellationToken cancellationToken) =>
        store.SetGrantsAsync(actor, id, command, cancellationToken);
    public Task<PagedResult<AccountAudit>> AuditAsync(AccountActor actor, int page, int pageSize, CancellationToken cancellationToken) =>
        store.AuditAsync(actor, page, pageSize, cancellationToken);
}
