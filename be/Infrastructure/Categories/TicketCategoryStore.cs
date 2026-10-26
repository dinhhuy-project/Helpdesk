using System.Text.Json;
using be.Application.Common.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Categories;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace be.Infrastructure.Categories;

public sealed class TicketCategoryStore(HelpdeskDbContext database, IPermissionChecker permissions,
    IAuthSessionStore sessions, TimeProvider clock) : ITicketCategoryStore
{
    public async Task<PagedResult<TicketCategoryDetails>> ListAsync(CategorySearch search, CancellationToken cancellationToken)
    {
        var query = Filter(search);
        var count = await query.CountAsync(cancellationToken);
        var items = await Project(Page(query, search)).ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<PagedResult<TicketCategoryLookup>> LookupAsync(CategorySearch search, CancellationToken cancellationToken)
    {
        var available = AvailableIds();
        var query = Filter(search).Where(x => available.Contains(x.Id));
        var count = await query.CountAsync(cancellationToken);
        var items = await Page(query, search).Select(x => new TicketCategoryLookup(x.Id, x.ParentId, x.Name,
            database.TicketCategories.Any(child => child.ParentId == x.Id && available.Contains(child.Id))))
            .ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<TicketCategoryDetails> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await Project(database.TicketCategories.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(cancellationToken)
        ?? throw Error(CategoryError.NotFound);

    public async Task<TicketCategoryDetails> CreateAsync(AccountActor actor, CreateTicketCategory command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        await CheckParentAsync(null, command.ParentId, cancellationToken);
        var category = new TicketCategory { Name = command.Name, Description = command.Description,
            ParentId = command.ParentId, CreatedAt = Now };
        database.TicketCategories.Add(category);
        Audit(actor, "TicketCategoryCreated", new { CategoryId = category.Id, category.ParentId });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(category.Id, cancellationToken);
    }

    public async Task<TicketCategoryDetails> UpdateAsync(AccountActor actor, Guid id, UpdateTicketCategory command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var category = await LockCategoryAsync(id, command.Version, cancellationToken);
        if (category.ParentId != command.ParentId) await CheckParentAsync(id, command.ParentId, cancellationToken);
        if (category.Name != command.Name || category.Description != command.Description || category.ParentId != command.ParentId)
        {
            var previousParentId = category.ParentId;
            category.Name = command.Name;
            category.Description = command.Description;
            category.ParentId = command.ParentId;
            category.UpdatedAt = Now;
            Audit(actor, "TicketCategoryUpdated", new { CategoryId = id, PreviousParentId = previousParentId,
                CurrentParentId = command.ParentId, Fields = new[] { "name", "description", "parentId" } });
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TicketCategoryDetails> SetStatusAsync(AccountActor actor, Guid id, TicketCategoryStatus command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var category = await LockCategoryAsync(id, command.Version, cancellationToken);
        if (command.IsActive) await CheckParentAsync(id, category.ParentId, cancellationToken);
        if (category.IsActive != command.IsActive)
        {
            var previous = category.IsActive;
            category.IsActive = command.IsActive;
            category.UpdatedAt = Now;
            // Descendant flags and existing Ticket.CategoryId remain unchanged.
            Audit(actor, "TicketCategoryStatusChanged", new { CategoryId = id, Previous = previous, Current = command.IsActive });
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private IQueryable<TicketCategory> Filter(CategorySearch search)
    {
        var query = database.TicketCategories.AsNoTracking();
        if (search.RootsOnly) query = query.Where(x => x.ParentId == null);
        else if (search.ParentId is not null) query = query.Where(x => x.ParentId == search.ParentId);
        if (search.IsActive is not null) query = query.Where(x => x.IsActive == search.IsActive);
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => x.NormalizedName.Contains(search.Search.ToLower()));
        return query;
    }
    private static IQueryable<TicketCategory> Page(IQueryable<TicketCategory> query, CategorySearch search)
    {
        var ordered = (search.SortBy, search.Descending) switch
        {
            ("createdAt", false) => query.OrderBy(x => x.CreatedAt),
            ("createdAt", true) => query.OrderByDescending(x => x.CreatedAt),
            (_, true) => query.OrderByDescending(x => x.NormalizedName),
            _ => query.OrderBy(x => x.NormalizedName)
        };
        return ordered.ThenBy(x => x.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize);
    }
    private IQueryable<TicketCategoryDetails> Project(IQueryable<TicketCategory> query)
    {
        var available = AvailableIds();
        return query.Select(x => new TicketCategoryDetails(x.Id, x.ParentId, x.Name, x.Description, x.IsActive,
            available.Contains(x.Id), database.TicketCategories.Any(child => child.ParentId == x.Id), x.CreatedAt, x.UpdatedAt, x.Version));
    }

    // Start at active roots and walk only active edges: no inactive-ancestor descendants,
    // or rootless legacy cycles, can enter lookup. UNION also bounds malformed repeated paths.
    private IQueryable<Guid> AvailableIds() => TicketCategoryAvailability.Query(database);

    private async Task CheckParentAsync(Guid? id, Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null) return;
        if (parentId == id) throw Error(CategoryError.CategoryCycle);
        if (id is not null)
        {
            // Distinct IDs terminate even if a direct SQL writer left a legacy cycle.
            var descendants = database.Database.SqlQuery<Guid>($"""
                WITH RECURSIVE descendants(id) AS (
                    SELECT id FROM helpdesk.ticket_categories WHERE id = {id.Value}
                    UNION
                    SELECT child.id FROM helpdesk.ticket_categories child JOIN descendants parent ON child.parent_id = parent.id
                ) SELECT id AS "Value" FROM descendants
                """);
            if (await descendants.AnyAsync(x => x == parentId.Value, cancellationToken)) throw Error(CategoryError.CategoryCycle);
        }
        if (!await AvailableIds().AnyAsync(x => x == parentId.Value, cancellationToken)) throw Error(CategoryError.InvalidParent);
    }

    private async Task<IDbContextTransaction> BeginAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize every reparent/status operation across instances; two opposing moves
            // must not both validate against the old tree. Shared with Identity/account administration.
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
            var user = await database.Users.FromSqlInterpolated($"SELECT *, xmin FROM identity.users WHERE id = {actor.UserId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (user is null || !await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken))
                throw Error(CategoryError.Unauthenticated);
            if (!await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, PermissionCodes.CategoryManage, cancellationToken))
                throw Error(CategoryError.InsufficientScope);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
    private async Task<TicketCategory> LockCategoryAsync(Guid id, uint version, CancellationToken cancellationToken)
    {
        var category = await database.TicketCategories.FromSqlInterpolated($"SELECT *, xmin FROM helpdesk.ticket_categories WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw Error(CategoryError.NotFound);
        if (category.Version != version) throw Error(CategoryError.VersionConflict);
        return category;
    }
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_ticket_categories_normalized_name" or "ix_ticket_categories_parent_id_normalized_name" })
        { throw Error(CategoryError.NameInUse); }
    }
    private void Audit(AccountActor actor, string action, object data) => database.IdentityAuditLogs.Add(
        new IdentityAuditLog { ActorId = actor.UserId, Action = action, Data = JsonSerializer.Serialize(data), CreatedAt = Now });
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static CategoryOperationException Error(CategoryError error) => new(error);
}
