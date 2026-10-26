using be.Application.Features.Accounts;

namespace be.Application.Features.SupportTeams;

public interface ISupportTeamStore
{
    Task<PagedResult<SupportTeamDetails>> ListAsync(AccountActor actor, TeamSearch search, CancellationToken cancellationToken);
    Task<SupportTeamDetails> GetAsync(AccountActor actor, Guid id, CancellationToken cancellationToken);
    Task<PagedResult<TeamMemberDetails>> MembersAsync(AccountActor actor, Guid id, TeamMemberSearch search, CancellationToken cancellationToken);
    Task<SupportTeamDetails> CreateAsync(AccountActor actor, CreateSupportTeam command, CancellationToken cancellationToken);
    Task<SupportTeamDetails> UpdateAsync(AccountActor actor, Guid id, UpdateSupportTeam command, CancellationToken cancellationToken);
    Task<SupportTeamDetails> SetStatusAsync(AccountActor actor, Guid id, SupportTeamStatus command, CancellationToken cancellationToken);
    Task<TeamMembershipResult> AddMemberAsync(AccountActor actor, Guid id, AddTeamMember command, CancellationToken cancellationToken);
    Task<TeamMembershipResult> SetMemberStatusAsync(AccountActor actor, Guid id, Guid userId, TeamMemberStatus command, CancellationToken cancellationToken);
    Task<TeamMembershipResult> SetLeadershipAsync(AccountActor actor, Guid id, Guid userId, TeamMemberLeadership command, CancellationToken cancellationToken);
}
