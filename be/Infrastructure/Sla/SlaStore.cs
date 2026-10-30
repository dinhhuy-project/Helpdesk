using System.Text.Json;
using be.Application.Common.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Sla;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Rules;
using be.Infrastructure.Persistence;
using be.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace be.Infrastructure.Sla;

public sealed class SlaStore(HelpdeskDbContext database, IPermissionChecker permissions, IAuthSessionStore sessions,
    TicketScope scope, TimeProvider clock) : ISlaStore
{
    public async Task<PagedResult<SlaPolicyDetails>> ListAsync(AccountActor actor, int page, int pageSize, CancellationToken cancellationToken)
    {
        await RequireManageAsync(actor, cancellationToken);
        var query = database.SlaPolicies.AsNoTracking();
        var count = await query.CountAsync(cancellationToken);
        var rows = await Project(query.OrderByDescending(x => x.IsActive).ThenBy(x => x.Priority == Domain.Enums.TicketPriority.Low ? 1 :
            x.Priority == Domain.Enums.TicketPriority.Normal ? 2 : x.Priority == Domain.Enums.TicketPriority.High ? 3 : 4).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new(rows, page, pageSize, count);
    }
    public async Task<SlaPolicyDetails> GetPolicyAsync(AccountActor actor, Guid id, CancellationToken cancellationToken)
    {
        await RequireManageAsync(actor, cancellationToken);
        return await ReadAsync(id, cancellationToken);
    }
    public async Task<SlaPolicyDetails> CreateAsync(AccountActor actor, SaveSlaPolicy command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        await RequireFreePriorityAsync(null, command.Priority, cancellationToken);
        var policy = new SlaPolicy { Name = command.Name, Priority = command.Priority, FirstResponseMinutes = command.FirstResponseMinutes,
            ResolutionMinutes = command.ResolutionMinutes, CreatedAt = Now };
        database.SlaPolicies.Add(policy);
        Audit(actor, "SlaPolicyCreated", policy, null);
        await SaveAsync(cancellationToken);
        var result = await ReadAsync(policy.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
    public async Task<SlaPolicyDetails> UpdateAsync(AccountActor actor, Guid id, uint version, SaveSlaPolicy command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var policy = await LockAsync(id, version, cancellationToken);
        if (policy.IsActive) await RequireFreePriorityAsync(id, command.Priority, cancellationToken);
        if (policy.Name != command.Name || policy.Priority != command.Priority || policy.FirstResponseMinutes != command.FirstResponseMinutes || policy.ResolutionMinutes != command.ResolutionMinutes)
        {
            var previous = Values(policy);
            policy.Name = command.Name; policy.Priority = command.Priority; policy.FirstResponseMinutes = command.FirstResponseMinutes;
            policy.ResolutionMinutes = command.ResolutionMinutes; policy.UpdatedAt = Now;
            Audit(actor, "SlaPolicyUpdated", policy, previous);
            await SaveAsync(cancellationToken);
        }
        var result = await ReadAsync(id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
    public async Task<SlaPolicyDetails> SetStatusAsync(AccountActor actor, Guid id, uint version, bool active, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var policy = await LockAsync(id, version, cancellationToken);
        if (active) await RequireFreePriorityAsync(id, policy.Priority, cancellationToken);
        if (policy.IsActive != active)
        {
            var previous = Values(policy);
            policy.IsActive = active; policy.UpdatedAt = Now;
            Audit(actor, "SlaPolicyStatusChanged", policy, previous);
            await SaveAsync(cancellationToken);
        }
        var result = await ReadAsync(id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
    public async Task<TicketSlaDetails> GetTicketAsync(AccountActor actor, Guid id, CancellationToken cancellationToken)
    {
        // Ticket status, read scope and SLA must describe one snapshot, including during resolve/reopen.
        await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw new SlaOperationException(SlaError.Unauthenticated);
        if (!await scope.HasReadGrantAsync(actor, cancellationToken)) throw new SlaOperationException(SlaError.Forbidden);
        var ticket = await scope.For(actor, TicketOperation.Read).AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.Status })
            .SingleOrDefaultAsync(cancellationToken) ?? throw new SlaOperationException(SlaError.NotFound);
        var readable = scope.For(actor, TicketOperation.Read).Select(x => x.Id);
        var sla = await database.TicketSlas.AsNoTracking().SingleOrDefaultAsync(x => x.TicketId == id && readable.Contains(x.TicketId), cancellationToken);
        var asOf = Now;
        var result = new TicketSlaDetails(ticket.Id, sla is null ? null : new(sla.SlaPolicyId, sla.PolicyName, sla.Priority, sla.FirstResponseMinutes, sla.ResolutionMinutes,
            sla.AppliedAt, sla.FirstResponseDueAt, sla.ResolutionDueAt, sla.FirstRespondedAt, sla.ResolvedAt,
            SlaRules.FirstResponseBreached(sla, ticket.Status, asOf), SlaRules.ResolutionBreached(sla, ticket.Status, asOf),
            sla.FirstResponseBreachedAt, sla.ResolutionBreachedAt), ticket.Status, asOf);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
    private async Task<IDbContextTransaction> BeginAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
            // Session rotation/logout also lock this row; validate only after acquiring it.
            var user = await database.Users.FromSqlInterpolated($"SELECT *, xmin FROM identity.users WHERE id = {actor.UserId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (user is null) throw new SlaOperationException(SlaError.Unauthenticated);
            await RequireManageAsync(actor, cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
    private async Task RequireManageAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw new SlaOperationException(SlaError.Unauthenticated);
        if (!await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, PermissionCodes.TicketManageSla, cancellationToken)) throw new SlaOperationException(SlaError.Forbidden);
    }
    private async Task<SlaPolicy> LockAsync(Guid id, uint version, CancellationToken cancellationToken)
    {
        var policy = await database.SlaPolicies.FromSqlInterpolated($"SELECT *, xmin FROM helpdesk.sla_policies WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new SlaOperationException(SlaError.NotFound);
        await database.Entry(policy).ReloadAsync(cancellationToken);
        if (policy.Version != version) throw new SlaOperationException(SlaError.VersionConflict);
        return policy;
    }
    private async Task RequireFreePriorityAsync(Guid? id, Domain.Enums.TicketPriority priority, CancellationToken cancellationToken)
    {
        if (await database.SlaPolicies.AnyAsync(x => x.Id != id && x.Priority == priority && x.IsActive, cancellationToken)) throw new SlaOperationException(SlaError.ActivePolicyExists);
    }
    private async Task<SlaPolicyDetails> ReadAsync(Guid id, CancellationToken cancellationToken) => await Project(database.SlaPolicies.AsNoTracking().Where(x => x.Id == id))
        .SingleOrDefaultAsync(cancellationToken) ?? throw new SlaOperationException(SlaError.NotFound);
    private static IQueryable<SlaPolicyDetails> Project(IQueryable<SlaPolicy> query) => query.Select(x => new SlaPolicyDetails(x.Id, x.Name, x.Priority,
        x.FirstResponseMinutes, x.ResolutionMinutes, x.IsActive, x.CreatedAt, x.UpdatedAt, x.Version));
    private void Audit(AccountActor actor, string action, SlaPolicy policy, object? previous) => database.IdentityAuditLogs.Add(new IdentityAuditLog
    { ActorId = actor.UserId, Action = action, CreatedAt = Now, Data = JsonSerializer.Serialize(new { PolicyId = policy.Id, Previous = previous, Current = Values(policy) }) });
    private static object Values(SlaPolicy policy) => new { policy.Name, Priority = policy.Priority.ToString(), policy.FirstResponseMinutes, policy.ResolutionMinutes, policy.IsActive };
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505", ConstraintName: "ix_sla_policies_priority" })
        { throw new SlaOperationException(SlaError.ActivePolicyExists); }
    }
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
