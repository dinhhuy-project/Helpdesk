using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Categories;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TestAccounts = Helpdesk.Persistence.Tests.AccountAdministrationTests.TestAccounts;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class TicketCategoryAdministrationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task TreeCanBeBrowsedAndReparentedWithStablePagingAndExplicitParent()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var root = await test.CreateAsync("Root A", description: " Description ");
        var other = await test.CreateAsync("Root B");
        var child = await test.CreateAsync("Child A", root.Id);
        await test.CreateAsync("Child B", root.Id);
        Assert.Equal("Description", root.Description);
        Assert.True(root.IsActive && root.IsAvailable && root.Version > 0);
        var roots = await test.Accounts.Admin.GetFromJsonAsync<PagedResult<TicketCategoryDetails>>(
            $"/api/v1/ticket-categories?rootsOnly=true&search={test.Prefix}&pageSize=1");
        Assert.Equal(2, roots!.TotalCount);
        Assert.Equal(root.Id, Assert.Single(roots.Items).Id);
        Assert.True(roots.Items[0].HasChildren);
        var page = await test.Accounts.Admin.GetFromJsonAsync<PagedResult<TicketCategoryDetails>>(
            $"/api/v1/ticket-categories?rootsOnly=true&search={test.Prefix}&pageSize=1&page=2");
        Assert.Equal(other.Id, Assert.Single(page!.Items).Id);
        var children = await test.Accounts.Admin.GetFromJsonAsync<PagedResult<TicketCategoryDetails>>($"/api/v1/ticket-categories?parentId={root.Id}");
        Assert.Equal(2, children!.TotalCount);
        using var update = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{child.Id}",
            new { child.Name, Description = "Changed", ParentId = other.Id, child.Version });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var changed = (await update.Content.ReadFromJsonAsync<TicketCategoryDetails>())!;
        Assert.Equal(other.Id, changed.ParentId);
        Assert.NotEqual(child.Version, changed.Version);
        await using var database = test.Accounts.Database();
        var audits = await database.IdentityAuditLogs.CountAsync(x => x.ActorId == test.Accounts.AdminUser.Id);
        using var noOp = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{child.Id}",
            new { changed.Name, changed.Description, changed.ParentId, changed.Version });
        Assert.Equal(changed, await noOp.Content.ReadFromJsonAsync<TicketCategoryDetails>());
        Assert.Equal(audits, await database.IdentityAuditLogs.CountAsync(x => x.ActorId == test.Accounts.AdminUser.Id));
        using var toRoot = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{child.Id}",
            new { changed.Name, changed.Description, ParentId = (Guid?)null, changed.Version });
        Assert.Equal(HttpStatusCode.OK, toRoot.StatusCode);
        Assert.Null((await toRoot.Content.ReadFromJsonAsync<TicketCategoryDetails>())!.ParentId);
        using var deletion = await test.Accounts.Admin.DeleteAsync($"/api/v1/ticket-categories/{child.Id}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deletion.StatusCode);
        using var missing = await test.Accounts.Admin.GetAsync($"/api/v1/ticket-categories/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [PostgresFact]
    public async Task NamesAreUniqueAmongSiblingsIncludingRootsAndInactiveAndFailedMoveRollsBackAudit()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var root = await test.CreateAsync("Same");
        var other = await test.CreateAsync("Other");
        var child = await test.CreateAsync("Same", root.Id);
        await test.CreateAsync("Same", other.Id); // Same name under a different parent is allowed.
        using var deactivate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/ticket-categories/{child.Id}/status", new { IsActive = false, child.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        foreach (var parentId in new Guid?[] { null, root.Id })
        {
            using var duplicate = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories",
                new { Name = " " + root.Name.ToUpperInvariant() + " ", ParentId = parentId });
            await ErrorAsync(duplicate, HttpStatusCode.Conflict, "NameInUse");
        }
        var current = await test.GetAsync(child.Id);
        await using var database = test.Accounts.Database();
        var audits = await database.IdentityAuditLogs.CountAsync(x => x.ActorId == test.Accounts.AdminUser.Id);
        using var move = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{child.Id}",
            new { current.Name, ParentId = other.Id, Description = "secret-marker", current.Version });
        await ErrorAsync(move, HttpStatusCode.Conflict, "NameInUse");
        Assert.DoesNotContain("secret-marker", await move.Content.ReadAsStringAsync());
        Assert.Equal(current, await test.GetAsync(child.Id));
        Assert.Equal(audits, await database.IdentityAuditLogs.CountAsync(x => x.ActorId == test.Accounts.AdminUser.Id));
    }

    [PostgresFact]
    public async Task ParentMustExistBeAvailableAndCannotBeSelfOrDescendant()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var root = await test.CreateAsync("Root");
        var child = await test.CreateAsync("Child", root.Id);
        var grandchild = await test.CreateAsync("Grandchild", child.Id);
        foreach (var parent in new[] { root.Id, child.Id, grandchild.Id })
        {
            using var cycle = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{root.Id}",
                new { root.Name, ParentId = parent, root.Version });
            await ErrorAsync(cycle, HttpStatusCode.BadRequest, "CategoryCycle");
        }
        using var missing = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = test.Prefix + "Invalid", ParentId = Guid.NewGuid() });
        await ErrorAsync(missing, HttpStatusCode.BadRequest, "InvalidParent");
        using var deactivate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/ticket-categories/{root.Id}/status", new { IsActive = false, root.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        foreach (var parent in new[] { root.Id, child.Id, grandchild.Id })
        {
            using var invalid = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = test.Prefix + "Invalid", ParentId = parent });
            await ErrorAsync(invalid, HttpStatusCode.BadRequest, "InvalidParent");
        }
        using var activateChild = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/ticket-categories/{child.Id}/status", new { IsActive = true, child.Version });
        await ErrorAsync(activateChild, HttpStatusCode.BadRequest, "InvalidParent");
        using var metadata = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{child.Id}",
            new { child.Name, child.ParentId, Description = "Historical edit", child.Version });
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
    }

    [PostgresFact]
    public async Task DeactivateHidesWholeBranchButPreservesFlagsVersionsAndHistoricalTicket()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var root = await test.CreateAsync("Root");
        var child = await test.CreateAsync("Child", root.Id);
        var grandchild = await test.CreateAsync("Grandchild", child.Id);
        var disabled = await test.CreateAsync("Disabled", root.Id);
        using var disabledResponse = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/ticket-categories/{disabled.Id}/status", new { IsActive = false, disabled.Version });
        Assert.Equal(HttpStatusCode.OK, disabledResponse.StatusCode);
        await using var database = test.Accounts.Database();
        var ticket = new Ticket { Title = "Historical category", Description = "History", CategoryId = grandchild.Id,
            RequesterId = test.Accounts.AdminUser.Id, CreatedByUserId = test.Accounts.AdminUser.Id };
        database.Tickets.Add(ticket);
        await database.SaveChangesAsync();
        try
        {
            using var deactivate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/ticket-categories/{root.Id}/status", new { IsActive = false, root.Version });
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
            var inactiveRoot = (await deactivate.Content.ReadFromJsonAsync<TicketCategoryDetails>())!;
            Assert.Empty((await test.LookupAsync())!.Items);
            var historicalChild = await test.GetAsync(child.Id);
            Assert.True(historicalChild.IsActive);
            Assert.False(historicalChild.IsAvailable);
            Assert.Equal(child.Version, historicalChild.Version);
            var historical = await database.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticket.Id);
            Assert.Equal(grandchild.Id, historical.CategoryId);
            Assert.Equal(ticket.Version, historical.Version);
            using var activate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/ticket-categories/{root.Id}/status", new { IsActive = true, inactiveRoot.Version });
            Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
            var lookup = await test.LookupAsync();
            Assert.Equal(3, lookup.TotalCount);
            Assert.DoesNotContain(lookup.Items, x => x.Id == disabled.Id);
            var rootLookup = Assert.Single(lookup.Items, x => x.Id == root.Id);
            Assert.True(rootLookup.HasChildren);
        }
        finally { await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteDeleteAsync(); }
    }

    [PostgresFact]
    public async Task MovingActiveSubtreeOutOfInactiveAncestorMakesItAvailableAgain()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var root = await test.CreateAsync("Root");
        var child = await test.CreateAsync("Child", root.Id);
        var grandchild = await test.CreateAsync("Grandchild", child.Id);
        var other = await test.CreateAsync("Other");
        using var deactivate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/ticket-categories/{root.Id}/status", new { IsActive = false, root.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        using var move = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{child.Id}", new { child.Name, ParentId = other.Id, child.Version });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.True((await test.GetAsync(grandchild.Id)).IsAvailable);
        Assert.Equal(grandchild.Version, (await test.GetAsync(grandchild.Id)).Version);
        var lookup = await test.Accounts.Admin.GetFromJsonAsync<PagedResult<TicketCategoryLookup>>($"/api/v1/ticket-categories/lookup?parentId={other.Id}");
        Assert.Equal(child.Id, Assert.Single(lookup!.Items).Id);
    }

    [PostgresFact]
    public async Task ConcurrentOpposingMovesCannotCreateCycle()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var first = await test.CreateAsync("First");
        var second = await test.CreateAsync("Second");
        var responses = await Task.WhenAll(
            test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{first.Id}", new { first.Name, ParentId = second.Id, first.Version }),
            test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{second.Id}", new { second.Name, ParentId = first.Id, second.Version }));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            await ErrorAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest), HttpStatusCode.BadRequest, "CategoryCycle");
            Assert.Equal(2, (await test.LookupAsync()).TotalCount);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task ConcurrentCreateAndUpdatesEnforceNameUniquenessAndVersion()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => test.Accounts.Admin.PostAsJsonAsync(
            "/api/v1/ticket-categories", new { Name = test.Prefix + "Concurrent" })));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            var created = (await Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<TicketCategoryDetails>())!;
            test.Own(created.Id);
            var updates = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => test.Accounts.Admin.PatchAsJsonAsync(
                $"/api/v1/ticket-categories/{created.Id}", new { created.Name, ParentId = (Guid?)null, Description = $"Edit {i}", created.Version })));
            try
            {
                Assert.Single(updates, x => x.StatusCode == HttpStatusCode.OK);
                await ErrorAsync(Assert.Single(updates, x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "VersionConflict");
            }
            finally { foreach (var response in updates) response.Dispose(); }
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task LookupAndAdministrationHaveSeparatePermissionsAndCurrentDatabaseGrants()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var category = await test.CreateAsync("Authorization");
        var requester = await test.Accounts.NewUserAsync();
        await test.Accounts.AssignAsync(requester.Id, await test.Accounts.RoleIdAsync(RoleNames.Requester));
        using var requestClient = await test.Accounts.ClientAsync(requester);
        using var lookup = await requestClient.GetAsync("/api/v1/ticket-categories/lookup");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        using var deniedList = await requestClient.GetAsync("/api/v1/ticket-categories");
        Assert.Equal(HttpStatusCode.Forbidden, deniedList.StatusCode);
        using var deniedCreate = await requestClient.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = "Forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);
        var manager = await test.Accounts.NewUserAsync();
        var role = await test.Accounts.CustomRoleAsync(PermissionCodes.CategoryManage);
        await test.Accounts.AssignAsync(manager.Id, role);
        using var client = await test.Accounts.ClientAsync(manager);
        using var detail = await client.GetAsync($"/api/v1/ticket-categories/{category.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var edit = await client.PatchAsJsonAsync($"/api/v1/ticket-categories/{category.Id}", new { category.Name, ParentId = (Guid?)null, Description = "Delegate", category.Version });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var noLookup = await client.GetAsync("/api/v1/ticket-categories/lookup");
        Assert.Equal(HttpStatusCode.Forbidden, noLookup.StatusCode);
        using var noRoleGrant = await client.GetAsync("/api/v1/identity/roles");
        Assert.Equal(HttpStatusCode.Forbidden, noRoleGrant.StatusCode);
        await using var database = test.Accounts.Database();
        await database.RolePermissions.Where(x => x.RoleId == role).ExecuteDeleteAsync();
        using var revoked = await client.GetAsync($"/api/v1/ticket-categories/{category.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        using var anonymous = test.Accounts.Factory.CreateClient();
        using var noToken = await anonymous.GetAsync("/api/v1/ticket-categories/lookup");
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
    }

    [PostgresTheory]
    [InlineData("grant")]
    [InlineData("session")]
    [InlineData("inactive")]
    public async Task MutationRechecksActorInsideTransaction(string change)
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var user = await test.Accounts.NewUserAsync();
        var role = await test.Accounts.CustomRoleAsync(PermissionCodes.CategoryManage);
        await test.Accounts.AssignAsync(user.Id, role);
        var login = await test.Accounts.LoginAsync(user);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var actor = new AccountActor(user.Id, Guid.Parse(token.Claims.Single(x => x.Type == "sid").Value));
        await using var database = test.Accounts.Database();
        if (change == "grant") await database.RolePermissions.Where(x => x.RoleId == role).ExecuteDeleteAsync();
        else if (change == "session") await database.RefreshTokens.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        else await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TicketCategoryService>();
        var error = await Assert.ThrowsAsync<CategoryOperationException>(() => service.CreateAsync(actor,
            new(test.Prefix + "Forbidden", null, null), CancellationToken.None));
        Assert.Equal(change == "grant" ? CategoryError.InsufficientScope : CategoryError.Unauthenticated, error.Error);
        Assert.False(await database.TicketCategories.AnyAsync(x => x.Name == test.Prefix + "Forbidden"));
    }

    [PostgresFact]
    public async Task CreateChildCannotUseParentDeactivatedByConcurrentTransaction()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var root = await test.CreateAsync("Root");
        await using var database = test.Accounts.Database();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)");
        await database.TicketCategories.Where(x => x.Id == root.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        var create = test.Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = test.Prefix + "Forbidden", ParentId = root.Id });
        await transaction.CommitAsync();
        using var response = await create;
        await ErrorAsync(response, HttpStatusCode.BadRequest, "InvalidParent");
    }

    [PostgresFact]
    public async Task LookupTerminatesAndExcludesRootlessCycleFromDirectSqlWriter()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var root = await test.CreateAsync("Root");
        var child = await test.CreateAsync("Child", root.Id);
        await using var database = test.Accounts.Database();
        await database.TicketCategories.Where(x => x.Id == root.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, child.Id));
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await test.Accounts.Admin.GetFromJsonAsync<PagedResult<TicketCategoryLookup>>(
                $"/api/v1/ticket-categories/lookup?search={test.Prefix}", cancellation.Token);
            Assert.Empty(result!.Items);
            Assert.False((await test.GetAsync(root.Id)).IsAvailable);
        }
        finally { await database.TicketCategories.Where(x => x.Id == root.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, (Guid?)null)); }
    }

    [PostgresTheory]
    [InlineData("blank")]
    [InlineData("long-name")]
    [InlineData("long-description")]
    [InlineData("overposting")]
    [InlineData("missing-version")]
    [InlineData("missing-parent")]
    [InlineData("missing-status")]
    [InlineData("empty-parent")]
    [InlineData("page")]
    [InlineData("page-size")]
    [InlineData("sort")]
    [InlineData("ambiguous-parent-filter")]
    public async Task ValidationRejectsInvalidAndOverpostedInput(string scenario)
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var category = await test.CreateAsync("Validation");
        using var response = scenario switch
        {
            "blank" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = "   " }),
            "long-name" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = new string('x', 201) }),
            "long-description" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = "Name", Description = new string('x', 2001) }),
            "overposting" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = "Name", IsActive = false }),
            "missing-version" => await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{category.Id}", new { category.Name, ParentId = (Guid?)null }),
            "missing-parent" => await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/ticket-categories/{category.Id}", new { category.Name, category.Version }),
            "missing-status" => await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/ticket-categories/{category.Id}/status", new { category.Version }),
            "empty-parent" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = "Name", ParentId = Guid.Empty }),
            "page" => await test.Accounts.Admin.GetAsync("/api/v1/ticket-categories?page=0"),
            "page-size" => await test.Accounts.Admin.GetAsync("/api/v1/ticket-categories/lookup?pageSize=101"),
            "sort" => await test.Accounts.Admin.GetAsync("/api/v1/ticket-categories?sortBy=private"),
            _ => await test.Accounts.Admin.GetAsync($"/api/v1/ticket-categories?rootsOnly=true&parentId={category.Id}")
        };
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [PostgresFact]
    public async Task SeedPreservesExistingGrantsAndCategoryGrantCommandIsIdempotentAndAudited()
    {
        await using var test = await Categories.CreateAsync(fixture.ConnectionString);
        var role = await test.Accounts.RoleIdAsync(RoleNames.SystemAdmin);
        await test.Accounts.SnapshotAsync(role);
        await using var database = test.Accounts.Database();
        var permission = await database.Permissions.Where(x => x.Code == PermissionCodes.CategoryManage).Select(x => x.Id).SingleAsync();
        var previous = await database.IdentityAuditLogs.Where(x => x.Action == "CategoryAdministrationEnabled").Select(x => x.Id).ToArrayAsync();
        try
        {
            await database.RolePermissions.Where(x => x.RoleId == role && x.PermissionId == permission).ExecuteDeleteAsync();
            var initializer = new IdentityInitializer(database, TestAccounts.Hasher());
            await initializer.SeedAsync();
            Assert.False(await database.RolePermissions.AnyAsync(x => x.RoleId == role && x.PermissionId == permission));
            await initializer.GrantCategoryAdministrationAsync();
            await initializer.GrantCategoryAdministrationAsync();
            Assert.True(await database.RolePermissions.AnyAsync(x => x.RoleId == role && x.PermissionId == permission));
            Assert.Equal(previous.Length + 1, await database.IdentityAuditLogs.CountAsync(x => x.Action == "CategoryAdministrationEnabled"));
        }
        finally { await database.IdentityAuditLogs.Where(x => x.Action == "CategoryAdministrationEnabled" && !previous.Contains(x.Id)).ExecuteDeleteAsync(); }
    }

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }
    private sealed class Categories(TestAccounts accounts) : IAsyncDisposable
    {
        private readonly List<Guid> _ids = [];
        public TestAccounts Accounts { get; } = accounts;
        public string Prefix { get; } = Guid.NewGuid().ToString("N") + "-";
        public static async Task<Categories> CreateAsync(string connection) => new(await TestAccounts.CreateAsync(connection));
        public void Own(Guid id) => _ids.Add(id);
        public async Task<TicketCategoryDetails> CreateAsync(string name, Guid? parent = null, string? description = null)
        {
            using var response = await Accounts.Admin.PostAsJsonAsync("/api/v1/ticket-categories", new { Name = " " + Prefix + name + " ", ParentId = parent, Description = description });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var category = (await response.Content.ReadFromJsonAsync<TicketCategoryDetails>())!;
            Own(category.Id);
            Assert.EndsWith($"/api/v1/ticket-categories/{category.Id}", response.Headers.Location?.ToString());
            return category;
        }
        public async Task<TicketCategoryDetails> GetAsync(Guid id) => (await Accounts.Admin.GetFromJsonAsync<TicketCategoryDetails>($"/api/v1/ticket-categories/{id}"))!;
        public async Task<PagedResult<TicketCategoryLookup>> LookupAsync() =>
            (await Accounts.Admin.GetFromJsonAsync<PagedResult<TicketCategoryLookup>>($"/api/v1/ticket-categories/lookup?search={Prefix}&pageSize=100"))!;
        public async ValueTask DisposeAsync()
        {
            await using var database = Accounts.Database();
            // Delete only test-owned leaves; never move duplicate sibling names into the root scope.
            while (await database.TicketCategories.AnyAsync(x => _ids.Contains(x.Id)))
            {
                var removed = await database.TicketCategories.Where(x => _ids.Contains(x.Id) &&
                    !database.TicketCategories.Any(child => child.ParentId == x.Id)).ExecuteDeleteAsync();
                if (removed == 0) throw new InvalidOperationException("Test-owned category tree could not be cleaned safely.");
            }
            await Accounts.DisposeAsync();
        }
    }
}
