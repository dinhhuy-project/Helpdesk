using System.Text.Json;
using be.Application.Common.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Application.Features.Departments;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace be.Infrastructure.Departments;

public sealed class DepartmentStore(HelpdeskDbContext database, IPermissionChecker permissions,
    IAuthSessionStore sessions, TimeProvider clock) : IDepartmentStore
{
    public async Task<PagedResult<DepartmentDetails>> ListAsync(DepartmentSearch search, CancellationToken cancellationToken)
    {
        var query = Filter(search);
        var count = await query.CountAsync(cancellationToken);
        var items = await Project(Page(query, search)).ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<PagedResult<DepartmentLookup>> LookupAsync(DepartmentSearch search, CancellationToken cancellationToken)
    {
        var query = Filter(search with { IsActive = true });
        var count = await query.CountAsync(cancellationToken);
        var items = await Page(query, search).Select(x => new DepartmentLookup(x.Id, x.Name)).ToListAsync(cancellationToken);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<DepartmentDetails> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await Project(database.Departments.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(cancellationToken)
        ?? throw Error(DepartmentError.NotFound);

    public async Task<DepartmentDetails> CreateAsync(AccountActor actor, CreateDepartment command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var department = new Department { Name = command.Name, Description = command.Description, CreatedAt = Now };
        database.Departments.Add(department);
        Audit(actor, "DepartmentCreated", new { DepartmentId = department.Id });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(department.Id, cancellationToken);
    }

    public async Task<DepartmentDetails> UpdateAsync(AccountActor actor, Guid id, UpdateDepartment command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var department = await LockDepartmentAsync(id, command.Version, cancellationToken);
        if (department.Name != command.Name || department.Description != command.Description)
        {
            department.Name = command.Name;
            department.Description = command.Description;
            department.UpdatedAt = Now;
            Audit(actor, "DepartmentUpdated", new { DepartmentId = id, Fields = new[] { "name", "description" } });
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<DepartmentDetails> SetStatusAsync(AccountActor actor, Guid id, DepartmentStatus command, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(actor, cancellationToken);
        var department = await LockDepartmentAsync(id, command.Version, cancellationToken);
        if (department.IsActive != command.IsActive)
        {
            var previous = department.IsActive;
            department.IsActive = command.IsActive;
            department.UpdatedAt = Now;
            // Preserve all existing user/team links, their statuses and sessions.
            Audit(actor, "DepartmentStatusChanged", new { DepartmentId = id, Previous = previous, Current = command.IsActive });
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private IQueryable<Department> Filter(DepartmentSearch search)
    {
        var query = database.Departments.AsNoTracking();
        if (search.IsActive is not null) query = query.Where(x => x.IsActive == search.IsActive);
        // Contains keeps search text literal, including percent/underscore characters.
        if (!string.IsNullOrEmpty(search.Search)) query = query.Where(x => x.NormalizedName.Contains(search.Search.ToLower()));
        return query;
    }

    private static IQueryable<Department> Page(IQueryable<Department> query, DepartmentSearch search)
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

    private static IQueryable<DepartmentDetails> Project(IQueryable<Department> query) => query.Select(x =>
        new DepartmentDetails(x.Id, x.Name, x.Description, x.IsActive, x.CreatedAt, x.UpdatedAt, x.Version));

    private async Task<IDbContextTransaction> BeginAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Same order as account administration: global lock, actor row, then target.
            // Serializes department deactivate with user assignment across API instances.
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
            var user = await database.Users.FromSqlInterpolated($"SELECT *, xmin FROM identity.users WHERE id = {actor.UserId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (user is null || !await sessions.IsActiveAsync(actor.UserId, actor.SessionId, cancellationToken))
                throw Error(DepartmentError.Unauthenticated);
            if (!await permissions.HasPermissionAsync(actor.UserId, actor.SessionId, PermissionCodes.DepartmentManage, cancellationToken))
                throw Error(DepartmentError.InsufficientScope);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private async Task<Department> LockDepartmentAsync(Guid id, uint expectedVersion, CancellationToken cancellationToken)
    {
        var department = await database.Departments
            .FromSqlInterpolated($"SELECT *, xmin FROM organization.departments WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw Error(DepartmentError.NotFound);
        if (department.Version != expectedVersion) throw Error(DepartmentError.VersionConflict);
        return department;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_departments_normalized_name" })
        { throw Error(DepartmentError.NameInUse); }
    }

    private void Audit(AccountActor actor, string action, object data) => database.IdentityAuditLogs.Add(
        new IdentityAuditLog { ActorId = actor.UserId, Action = action, Data = JsonSerializer.Serialize(data), CreatedAt = Now });
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static DepartmentOperationException Error(DepartmentError error) => new(error);
}
