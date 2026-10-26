using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Tickets;
using be.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Tickets;

public sealed class TicketReadStore(TicketScope scope, IAuthSessionStore sessions, ITicketAccessChecker access) : ITicketReadStore
{
    public async Task<PagedResult<TicketSummary>> ListAsync(AccountActor actor, TicketSearch search, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, cancellationToken);
        // Scope before filtering, counting, paging or projecting any Ticket fields.
        var query = scope.For(actor, TicketOperation.Read).AsNoTracking();
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => x.Title.ToLower().Contains(search.Search.ToLower()));
        if (search.Status is not null) query = query.Where(x => x.Status == search.Status);
        if (search.Priority is not null) query = query.Where(x => x.Priority == search.Priority);
        if (search.TeamId is not null) query = query.Where(x => x.AssignedTeamId == search.TeamId);
        if (search.CategoryId is not null) query = query.Where(x => x.CategoryId == search.CategoryId);
        if (search.TicketNumber is not null) query = query.Where(x => x.TicketNumber == search.TicketNumber);
        if (search.RequesterId is not null) query = query.Where(x => x.RequesterId == search.RequesterId);
        if (search.AgentId is not null) query = query.Where(x => x.AssignedAgentId == search.AgentId);
        if (search.Unassigned is not null) query = query.Where(x => (x.AssignedAgentId == null) == search.Unassigned);
        if (search.CreatedFrom is not null) query = query.Where(x => x.CreatedAt >= search.CreatedFrom.Value.UtcDateTime);
        if (search.CreatedTo is not null) query = query.Where(x => x.CreatedAt < search.CreatedTo.Value.UtcDateTime);
        var count = await query.CountAsync(cancellationToken);
        // Priority is stored as text. Sort by its explicit rank rather than lexical enum names.
        var ordered = (search.SortBy, search.Descending) switch
        {
            ("ticketNumber", false) => query.OrderBy(x => x.TicketNumber),
            ("ticketNumber", true) => query.OrderByDescending(x => x.TicketNumber),
            ("updatedAt", false) => query.OrderBy(x => x.UpdatedAt ?? x.CreatedAt),
            ("updatedAt", true) => query.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt),
            ("priority", false) => query.OrderBy(x => x.Priority == TicketPriority.Urgent ? 4 : x.Priority == TicketPriority.High ? 3 : x.Priority == TicketPriority.Normal ? 2 : 1),
            ("priority", true) => query.OrderByDescending(x => x.Priority == TicketPriority.Urgent ? 4 : x.Priority == TicketPriority.High ? 3 : x.Priority == TicketPriority.Normal ? 2 : 1),
            (_, false) => query.OrderBy(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.CreatedAt)
        };
        var items = await ordered.ThenBy(x => x.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(x => new TicketSummary(x.Id, x.TicketNumber, x.Title, x.Status, x.Priority, x.RequesterId,
                x.CategoryId, x.AssignedTeamId, x.AssignedAgentId, x.CreatedAt, x.UpdatedAt, x.Version)).ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<TicketDetails> GetAsync(AccountActor actor, Guid id, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, cancellationToken);
        return await scope.For(actor, TicketOperation.Read).AsNoTracking().Where(x => x.Id == id)
            .Select(x => new TicketDetails(x.Id, x.TicketNumber, x.Title, x.Description, x.Status, x.Priority, x.RequesterId,
                x.CategoryId, x.AssignedTeamId, x.AssignedAgentId, x.ResolutionSummary, x.CreatedAt, x.UpdatedAt,
                x.ResolvedAt, x.ClosedAt, x.Version)).SingleOrDefaultAsync(cancellationToken) ?? throw Error(TicketError.NotFound);
    }

    public async Task<TicketAccess> AccessAsync(AccountActor actor, Guid id, CancellationToken cancellationToken)
    {
        await RequireReadAsync(actor, cancellationToken);
        var result = await access.GetAsync(actor, id, cancellationToken);
        return result.CanRead ? result : throw Error(TicketError.NotFound);
    }
    private async Task RequireReadAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken)) throw Error(TicketError.Unauthenticated);
        if (!await scope.HasReadGrantAsync(actor, cancellationToken)) throw Error(TicketError.Forbidden);
    }
    private static TicketOperationException Error(TicketError error) => new(error);
}
