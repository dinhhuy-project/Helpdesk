using be.Application.Features.Accounts;
using be.Domain.Enums;
using be.Domain.Rules;

namespace be.Application.Features.Sla;

public enum SlaError { InvalidInput, NotFound, Unauthenticated, Forbidden, VersionConflict, ActivePolicyExists }
public sealed class SlaOperationException(SlaError error) : Exception("SLA operation rejected.")
{
    public SlaError Error { get; } = error;
}
public sealed record SlaPolicyDetails(Guid Id, string Name, TicketPriority Priority, int FirstResponseMinutes,
    int ResolutionMinutes, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, uint Version);
public sealed record SaveSlaPolicy(string Name, TicketPriority Priority, int FirstResponseMinutes, int ResolutionMinutes);
public sealed record TicketSlaSnapshot(Guid PolicyId, string? PolicyName, TicketPriority? Priority, int? FirstResponseMinutes, int? ResolutionMinutes,
    DateTime AppliedAt, DateTime FirstResponseDueAt, DateTime ResolutionDueAt, DateTime? FirstRespondedAt, DateTime? ResolvedAt,
    bool FirstResponseBreached, bool ResolutionBreached, DateTime? FirstResponseBreachedAt, DateTime? ResolutionBreachedAt);
public sealed record TicketSlaDetails(Guid TicketId, TicketSlaSnapshot? Sla, TicketStatus Status, DateTime AsOf);
public interface ISlaStore
{
    Task<PagedResult<SlaPolicyDetails>> ListAsync(AccountActor actor, int page, int pageSize, CancellationToken cancellationToken);
    Task<SlaPolicyDetails> GetPolicyAsync(AccountActor actor, Guid id, CancellationToken cancellationToken);
    Task<SlaPolicyDetails> CreateAsync(AccountActor actor, SaveSlaPolicy command, CancellationToken cancellationToken);
    Task<SlaPolicyDetails> UpdateAsync(AccountActor actor, Guid id, uint version, SaveSlaPolicy command, CancellationToken cancellationToken);
    Task<SlaPolicyDetails> SetStatusAsync(AccountActor actor, Guid id, uint version, bool active, CancellationToken cancellationToken);
    Task<TicketSlaDetails> GetTicketAsync(AccountActor actor, Guid id, CancellationToken cancellationToken);
}
public sealed class SlaService(ISlaStore store)
{
    public Task<PagedResult<SlaPolicyDetails>> ListAsync(AccountActor actor, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) throw new SlaOperationException(SlaError.InvalidInput);
        return store.ListAsync(actor, page, pageSize, cancellationToken);
    }
    public Task<SlaPolicyDetails> GetPolicyAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) => store.GetPolicyAsync(actor, id, cancellationToken);
    public Task<SlaPolicyDetails> CreateAsync(AccountActor actor, SaveSlaPolicy command, CancellationToken cancellationToken) => store.CreateAsync(actor, Validate(command), cancellationToken);
    public Task<SlaPolicyDetails> UpdateAsync(AccountActor actor, Guid id, uint version, SaveSlaPolicy command, CancellationToken cancellationToken)
    {
        if (version == 0) throw new SlaOperationException(SlaError.InvalidInput);
        return store.UpdateAsync(actor, id, version, Validate(command), cancellationToken);
    }
    public Task<SlaPolicyDetails> SetStatusAsync(AccountActor actor, Guid id, uint version, bool active, CancellationToken cancellationToken)
    {
        if (version == 0) throw new SlaOperationException(SlaError.InvalidInput);
        return store.SetStatusAsync(actor, id, version, active, cancellationToken);
    }
    public Task<TicketSlaDetails> GetTicketAsync(AccountActor actor, Guid id, CancellationToken cancellationToken) => store.GetTicketAsync(actor, id, cancellationToken);
    private static SaveSlaPolicy Validate(SaveSlaPolicy command)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 200 || !Enum.IsDefined(command.Priority) ||
            !SlaRules.ValidDurations(command.FirstResponseMinutes, command.ResolutionMinutes)) throw new SlaOperationException(SlaError.InvalidInput);
        return command with { Name = command.Name.Trim() };
    }
}
