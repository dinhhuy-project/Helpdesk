using be.Application.Features.Accounts;
using be.Application.Features.Departments;

namespace be.Application.Features.SupportTeams;

public sealed class SupportTeamService(ISupportTeamStore store)
{
    public Task<TeamAdministrationAccess> AccessAsync(AccountActor actor, Guid? id, CancellationToken cancellationToken) =>
        store.AccessAsync(actor, id, cancellationToken);
    public Task<PagedResult<DepartmentLookup>> DepartmentsAsync(AccountActor actor, TeamLookupSearch search, CancellationToken cancellationToken) =>
        store.DepartmentsAsync(actor, Lookup(search), cancellationToken);
    public Task<PagedResult<TeamMemberCandidate>> CandidatesAsync(AccountActor actor, Guid id, TeamLookupSearch search, CancellationToken cancellationToken) =>
        store.CandidatesAsync(actor, id, Lookup(search), cancellationToken);
    private static TeamLookupSearch Lookup(TeamLookupSearch search)
    {
        Page(search.Page, search.PageSize);
        if (search.Search?.Length > 200) Invalid();
        return search with { Search = search.Search?.Trim() };
    }
    public Task<PagedResult<SupportTeamDetails>> ListAsync(AccountActor actor, TeamSearch search, CancellationToken cancellationToken)
    {
        Page(search.Page, search.PageSize);
        if (search.Search?.Length > 200 || search.DepartmentId == Guid.Empty || search.SortBy is not ("name" or "createdAt")) Invalid();
        return store.ListAsync(actor, search with { Search = search.Search?.Trim(' ') }, cancellationToken);
    }
    public Task<SupportTeamDetails> GetAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) => store.GetAsync(actor, id, cancellationToken);
    public Task<PagedResult<TeamMemberDetails>> MembersAsync(AccountActor actor, Guid id, TeamMemberSearch search, CancellationToken cancellationToken)
    {
        Page(search.Page, search.PageSize);
        return store.MembersAsync(actor, id, search, cancellationToken);
    }
    public Task<SupportTeamDetails> CreateAsync(AccountActor actor, CreateSupportTeam command, CancellationToken cancellationToken)
    {
        if (command.DepartmentId == Guid.Empty) Invalid();
        return store.CreateAsync(actor, command with { Name = Name(command.Name), Description = Description(command.Description) }, cancellationToken);
    }
    public Task<SupportTeamDetails> UpdateAsync(AccountActor actor, Guid id, UpdateSupportTeam command, CancellationToken cancellationToken)
    {
        Version(command.Version);
        return store.UpdateAsync(actor, id, command with { Name = Name(command.Name), Description = Description(command.Description) }, cancellationToken);
    }
    public Task<SupportTeamDetails> SetStatusAsync(AccountActor actor, Guid id, SupportTeamStatus command, CancellationToken cancellationToken)
    {
        Version(command.Version);
        return store.SetStatusAsync(actor, id, command, cancellationToken);
    }
    public Task<TeamMembershipResult> AddMemberAsync(AccountActor actor, Guid id, AddTeamMember command, CancellationToken cancellationToken)
    {
        Version(command.TeamVersion);
        if (command.UserId == Guid.Empty) Invalid();
        return store.AddMemberAsync(actor, id, command, cancellationToken);
    }
    public Task<TeamMembershipResult> SetMemberStatusAsync(AccountActor actor, Guid id, Guid userId, TeamMemberStatus command, CancellationToken cancellationToken)
    {
        Version(command.Version); Version(command.TeamVersion);
        return store.SetMemberStatusAsync(actor, id, userId, command, cancellationToken);
    }
    public Task<TeamMembershipResult> SetLeadershipAsync(AccountActor actor, Guid id, Guid userId, TeamMemberLeadership command, CancellationToken cancellationToken)
    {
        Version(command.Version); Version(command.TeamVersion);
        return store.SetLeadershipAsync(actor, id, userId, command, cancellationToken);
    }
    private static string Name(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200
        ? value.Trim(' ') : throw new TeamOperationException(TeamError.InvalidInput);
    private static string? Description(string? value) => value?.Length > 2000
        ? throw new TeamOperationException(TeamError.InvalidInput) : string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Page(int page, int pageSize) { if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) Invalid(); }
    private static void Version(uint version) { if (version == 0) Invalid(); }
    private static void Invalid() => throw new TeamOperationException(TeamError.InvalidInput);
}
