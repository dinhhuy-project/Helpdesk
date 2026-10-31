using System.Text.Json;
using be.Application.Common.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Community;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace be.Infrastructure.Community;

public sealed class CommunityStore(HelpdeskDbContext database, IAuthSessionStore sessions, IPermissionChecker permissions,
    ITicketAccessChecker tickets, TimeProvider clock, IHubContext<PublicCommunityHub> hub, ILogger<CommunityStore> logger) : ICommunityStore
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Manage(CommunityKind kind) => kind == CommunityKind.Knowledge ? PermissionCodes.KnowledgeManage : PermissionCodes.IncidentManage;
    private static string Publish(CommunityKind kind) => kind == CommunityKind.Knowledge ? PermissionCodes.KnowledgePublish : PermissionCodes.IncidentPublish;
    private static CommunityOperationException Error(CommunityError error) => new(error);

    public async Task<KnowledgeImportResult> ImportAsync(AccountActor actor, ImportedKnowledgeDraft draft, CancellationToken ct)
    {
        await using var tx = await BeginAsync(actor, CommunityKind.Knowledge, PermissionCodes.KnowledgeManage, ct);
        var existing = await database.CommunityEntries.SingleOrDefaultAsync(x => x.Id == draft.Id, ct);
        if (existing is not null)
        {
            // Create-once imports never overwrite an editor's work or a published snapshot, even after retries.
            await tx.CommitAsync(ct);
            return new(Managed(existing), true);
        }
        var row = new CommunityEntry { Id = draft.Id, Kind = CommunityKind.Knowledge, Title = draft.Title,
            Summary = "Bản nháp nhập từ BookStack; cần kiểm tra nội dung, ghi công và quyền công khai trước khi xuất bản.",
            Content = draft.Content, Topic = "Hướng dẫn", CreatedById = actor.UserId, CreatedAt = Now, UpdatedAt = Now };
        database.CommunityEntries.Add(row);
        database.IdentityAuditLogs.Add(new IdentityAuditLog { ActorId = actor.UserId, Action = "KnowledgeImported", CreatedAt = Now,
            Data = JsonSerializer.Serialize(new { EntryId = row.Id, Source = "BookStack", draft.SourceId, draft.PageId, draft.Revision }) });
        await database.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(Managed(row), false);
    }

    public async Task<PagedResult<PublicCommunityEntry>> PublicListAsync(CommunityKind kind, CommunitySearch search, CancellationToken ct)
    {
        var query = Published(kind);
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => EF.Functions.ToTsVector("simple",
            (x.PublicTitle ?? "") + " " + (x.PublicSummary ?? "") + " " + (x.PublicContent ?? "") + " " + (x.PublicTopic ?? ""))
            .Matches(EF.Functions.PlainToTsQuery("simple", search.Search)));
        if (!string.IsNullOrEmpty(search.Topic)) query = query.Where(x => x.PublicTopic == search.Topic);
        if (kind == CommunityKind.Incident && search.Active is not null)
            query = query.Where(x => (x.PublicStatus != IncidentStatus.Resolved) == search.Active);
        var count = await query.CountAsync(ct);
        var items = await PublicProjection(query.OrderByDescending(x => x.PublishedAt).ThenBy(x => x.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize), false).ToListAsync(ct);
        return new(items, search.Page, search.PageSize, count);
    }
    public async Task<PublicCommunityEntry> PublicGetAsync(CommunityKind kind, Guid id, CancellationToken ct) =>
        await PublicProjection(Published(kind).Where(x => x.Id == id), true).SingleOrDefaultAsync(ct) ?? throw Error(CommunityError.NotFound);
    public async Task<PagedResult<PublicIncidentUpdate>> UpdatesAsync(Guid id, CommunitySearch search, CancellationToken ct)
    {
        var published = Published(CommunityKind.Incident).Where(x => x.Id == id).Select(x => x.Id);
        if (!await published.AnyAsync(ct)) throw Error(CommunityError.NotFound);
        var query = database.IncidentUpdates.AsNoTracking().Where(x => published.Contains(x.EntryId));
        return new(await query.OrderByDescending(x => x.Revision).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(x => new PublicIncidentUpdate(x.Id, x.Message, x.Status.ToString(), x.Revision, x.CreatedAt)).ToListAsync(ct),
            search.Page, search.PageSize, await query.CountAsync(ct));
    }
    public async Task<PagedResult<ManagedCommunityEntry>> ManageListAsync(AccountActor actor, CommunityKind kind, CommunitySearch search, CancellationToken ct)
    {
        await RequireAsync(actor, kind, null, ct);
        var query = database.CommunityEntries.AsNoTracking().Where(x => x.Kind == kind);
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => x.Title.ToLower().Contains(search.Search.ToLower()));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(ct);
        await RequireAsync(actor, kind, null, ct);
        return new(rows.Select(Managed).ToArray(), search.Page, search.PageSize, count);
    }
    public async Task<ManagedCommunityEntry> ManageGetAsync(AccountActor actor, CommunityKind kind, Guid id, CancellationToken ct)
    {
        await RequireAsync(actor, kind, null, ct);
        var row = await database.CommunityEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == kind && x.Id == id, ct) ?? throw Error(CommunityError.NotFound);
        await RequireAsync(actor, kind, null, ct);
        return Managed(row);
    }
    public async Task<ManagedCommunityEntry> SaveAsync(AccountActor actor, CommunityKind kind, Guid? id, SaveCommunityEntry command, CancellationToken ct)
    {
        await using var tx = await BeginAsync(actor, kind, Manage(kind), ct);
        var row = id is null ? new CommunityEntry { Kind = kind, Title = command.Title, Summary = command.Summary, Content = command.Content,
            Topic = command.Topic, CreatedById = actor.UserId, CreatedAt = Now } : await LockAsync(kind, id.Value, command.Version, ct);
        row.Title = command.Title; row.Summary = command.Summary; row.Content = command.Content; row.Topic = command.Topic;
        row.Status = kind == CommunityKind.Incident ? Enum.Parse<IncidentStatus>(command.Status!) : null;
        row.UpdatedAt = Now;
        if (id is null) database.CommunityEntries.Add(row);
        Audit(actor, "CommunityDraftSaved", row);
        await database.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Managed(row);
    }
    public async Task<ManagedCommunityEntry> FromTicketAsync(AccountActor actor, Guid ticketId, CancellationToken ct)
    {
        await using var tx = await BeginAsync(actor, CommunityKind.Knowledge, PermissionCodes.KnowledgeManage, ct);
        if (!await tickets.CanAsync(actor, ticketId, TicketOperation.Read, ct)) throw Error(CommunityError.NotFound);
        var ticket = await database.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId, ct);
        if (ticket is null || ticket.Status is not (TicketStatus.Resolved or TicketStatus.Closed) || string.IsNullOrWhiteSpace(ticket.ResolutionSummary)) throw Error(CommunityError.InvalidInput);
        // Ticket titles allow 300 UTF-16 units; the public editor contract allows 200.
        var title = ticket.Title.Length <= 200 ? ticket.Title : ticket.Title[..(char.IsHighSurrogate(ticket.Title[199]) ? 199 : 200)];
        var row = new CommunityEntry { Kind = CommunityKind.Knowledge, Title = title, Summary = "Bản nháp từ kết quả xử lý Ticket; cần biên soạn và duyệt lại trước khi công khai.",
            Content = ticket.ResolutionSummary, Topic = "Hướng dẫn", SourceTicketId = ticket.Id, CreatedById = actor.UserId, CreatedAt = Now, UpdatedAt = Now };
        database.CommunityEntries.Add(row);
        Audit(actor, "KnowledgeDraftFromTicket", row);
        await database.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Managed(row);
    }
    public async Task<ManagedCommunityEntry> PublishAsync(AccountActor actor, CommunityKind kind, Guid id, PublishCommunityEntry command, CancellationToken ct)
    {
        await using var tx = await BeginAsync(actor, kind, Publish(kind), ct);
        var row = await LockAsync(kind, id, command.Version, ct);
        if (kind == CommunityKind.Incident && !CanTransition(row.PublicStatus, row.Status)) throw Error(CommunityError.InvalidTransition);
        row.PublicTitle = row.Title; row.PublicSummary = row.Summary; row.PublicContent = row.Content; row.PublicTopic = row.Topic;
        row.PublicStatus = row.Status; row.IsPublished = true; row.PublicRevision++; row.PublishedAt = Now; row.UpdatedAt = Now;
        if (kind == CommunityKind.Incident) database.IncidentUpdates.Add(new IncidentUpdate { EntryId = id,
            Message = string.IsNullOrWhiteSpace(command.Message) ? row.Summary : command.Message, Status = row.Status!.Value, Revision = row.PublicRevision, CreatedAt = Now });
        Audit(actor, "CommunityPublished", row);
        await database.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (row.PublicRevision > 0) await ChangedAsync(kind, id);
        return Managed(row);
    }
    public async Task<ManagedCommunityEntry> WithdrawAsync(AccountActor actor, CommunityKind kind, Guid id, uint version, CancellationToken ct)
    {
        await using var tx = await BeginAsync(actor, kind, Publish(kind), ct);
        var row = await LockAsync(kind, id, version, ct);
        row.IsPublished = false; row.UpdatedAt = Now;
        Audit(actor, "CommunityWithdrawn", row);
        await database.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (row.PublicRevision > 0) await ChangedAsync(kind, id);
        return Managed(row);
    }
    public async Task<CommunityParticipation> ParticipationAsync(AccountActor actor, CommunityKind kind, Guid id, CancellationToken ct)
    {
        await RequireSessionAsync(actor, ct);
        if (!await Published(kind).AnyAsync(x => x.Id == id, ct)) throw Error(CommunityError.NotFound);
        var feedback = await database.KnowledgeFeedback.AsNoTracking().SingleOrDefaultAsync(x => x.EntryId == id && x.UserId == actor.UserId, ct);
        return new(await database.IncidentAffectedUsers.AnyAsync(x => x.EntryId == id && x.UserId == actor.UserId, ct), feedback?.Helpful, feedback?.Solved ?? false);
    }
    public async Task<CommunityParticipation> ParticipateAsync(AccountActor actor, CommunityKind kind, Guid id, bool value, bool solved, CancellationToken ct)
    {
        await using var tx = await BeginAsync(actor, kind, null, ct, sessionOnly: true);
        var row = await database.CommunityEntries.SingleOrDefaultAsync(x => x.Id == id && x.Kind == kind && x.IsPublished, ct) ?? throw Error(CommunityError.NotFound);
        if (kind == CommunityKind.Incident)
        {
            var previous = await database.IncidentAffectedUsers.SingleOrDefaultAsync(x => x.EntryId == id && x.UserId == actor.UserId, ct);
            if (value && previous is null)
            {
                if (row.PublicStatus == IncidentStatus.Resolved) throw Error(CommunityError.InvalidInput);
                database.IncidentAffectedUsers.Add(new IncidentAffectedUser { EntryId = id, UserId = actor.UserId, CreatedAt = Now });
            }
            if (!value && previous is not null) database.IncidentAffectedUsers.Remove(previous);
        }
        else
        {
            var feedback = await database.KnowledgeFeedback.SingleOrDefaultAsync(x => x.EntryId == id && x.UserId == actor.UserId, ct);
            if (feedback is null) { feedback = new KnowledgeFeedback { EntryId = id, UserId = actor.UserId }; database.KnowledgeFeedback.Add(feedback); }
            feedback.Helpful = value; feedback.Solved = solved; feedback.UpdatedAt = Now;
        }
        await database.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await ChangedAsync(kind, id);
        return await ParticipationAsync(actor, kind, id, ct);
    }

    private IQueryable<CommunityEntry> Published(CommunityKind kind) => database.CommunityEntries.AsNoTracking().Where(x => x.Kind == kind && x.IsPublished);
    private IQueryable<PublicCommunityEntry> PublicProjection(IQueryable<CommunityEntry> query, bool content) => query.Select(x => new PublicCommunityEntry(
        x.Id, x.Kind.ToString(), x.PublicTitle!, x.PublicSummary!, content ? x.PublicContent : null, x.PublicTopic!,
        x.PublicStatus == null ? null : x.PublicStatus.ToString(), x.PublicRevision, x.PublishedAt!.Value,
        (x.Kind == CommunityKind.Knowledge ? "/knowledge/" : "/incidents/") + x.Id,
        database.IncidentAffectedUsers.Count(a => a.EntryId == x.Id), database.KnowledgeFeedback.Count(f => f.EntryId == x.Id && f.Helpful),
        database.KnowledgeFeedback.Count(f => f.EntryId == x.Id && f.Solved)));
    private static ManagedCommunityEntry Managed(CommunityEntry x) => new(x.Id, x.Kind.ToString(), x.Title, x.Summary, x.Content, x.Topic,
        x.Status?.ToString(), x.IsPublished, x.PublicRevision, x.PublishedAt, x.Version, x.SourceTicketId, x.UpdatedAt);
    private async Task<CommunityEntry> LockAsync(CommunityKind kind, Guid id, uint version, CancellationToken ct)
    {
        var row = await database.CommunityEntries.FromSqlInterpolated($"SELECT *, xmin FROM helpdesk.community_entries WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(x => x.Kind == kind, ct) ?? throw Error(CommunityError.NotFound);
        if (row.Version != version) throw Error(CommunityError.VersionConflict);
        return row;
    }
    private async Task RequireSessionAsync(AccountActor actor, CancellationToken ct)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, ct)) throw Error(CommunityError.Unauthenticated);
    }
    private async Task RequireAsync(AccountActor actor, CommunityKind kind, string? permission, CancellationToken ct)
    {
        await RequireSessionAsync(actor, ct);
        var allowed = permission is not null ? await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, permission, ct) :
            await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, Manage(kind), ct) || await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, Publish(kind), ct);
        if (!allowed) throw Error(CommunityError.InsufficientScope);
    }
    private async Task<IDbContextTransaction> BeginAsync(AccountActor actor, CommunityKind kind, string? permission, CancellationToken ct, bool sessionOnly = false)
    {
        var tx = await database.Database.BeginTransactionAsync(ct);
        try
        {
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", ct);
            var user = await database.Users.FromSqlInterpolated($"SELECT *, xmin FROM identity.users WHERE id = {actor.UserId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (user is null || !user.IsActive) throw Error(CommunityError.Unauthenticated);
            if (sessionOnly) await RequireSessionAsync(actor, ct); else await RequireAsync(actor, kind, permission, ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    private void Audit(AccountActor actor, string action, CommunityEntry entry) => database.IdentityAuditLogs.Add(new IdentityAuditLog
    { ActorId = actor.UserId, Action = action, CreatedAt = Now, Data = JsonSerializer.Serialize(new { EntryId = entry.Id, Kind = entry.Kind.ToString(), entry.PublicRevision }) });
    private async Task ChangedAsync(CommunityKind kind, Guid id)
    {
        try { await hub.Clients.Group(PublicCommunityHub.Group).SendAsync("CommunityChanged", new { Kind = kind.ToString(), Id = id }, CancellationToken.None); }
        catch (Exception error) { logger.LogWarning("Community invalidation failed ({Type}); clients also refetch periodically.", error.GetType().Name); }
    }
    private static bool CanTransition(IncidentStatus? from, IncidentStatus? to) => from == to || (from, to) switch
    {
        (null, IncidentStatus.Investigating) => true,
        (IncidentStatus.Investigating, IncidentStatus.Identified or IncidentStatus.Monitoring or IncidentStatus.Resolved) => true,
        (IncidentStatus.Identified, IncidentStatus.Monitoring or IncidentStatus.Resolved) => true,
        (IncidentStatus.Monitoring, IncidentStatus.Investigating or IncidentStatus.Resolved) => true,
        (IncidentStatus.Resolved, IncidentStatus.Investigating) => true,
        _ => false
    };
}
