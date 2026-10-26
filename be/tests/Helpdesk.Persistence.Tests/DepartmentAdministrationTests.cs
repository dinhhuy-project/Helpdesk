using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Departments;
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
public class DepartmentAdministrationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task CreateReadUpdateAndStablePagingUseDtosAndVersion()
    {
        await using var test = await Departments.CreateAsync(fixture.ConnectionString);
        var first = await test.CreateAsync("A", " Description ");
        var second = await test.CreateAsync("B");
        Assert.True(first.IsActive);
        Assert.True(first.Version > 0);
        Assert.Equal("Description", first.Description);
        var list = await test.Accounts.Admin.GetFromJsonAsync<PagedResult<DepartmentDetails>>(
            $"/api/v1/departments?search={test.Prefix}&pageSize=1");
        Assert.Equal(2, list!.TotalCount);
        Assert.Equal(first.Id, Assert.Single(list.Items).Id);
        var page = await test.Accounts.Admin.GetFromJsonAsync<PagedResult<DepartmentDetails>>(
            $"/api/v1/departments?search={test.Prefix}&pageSize=1&page=2");
        Assert.Equal(second.Id, Assert.Single(page!.Items).Id);
        using var update = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/departments/{first.Id}",
            new { Name = first.Name, Description = "Updated", first.Version });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var changed = (await update.Content.ReadFromJsonAsync<DepartmentDetails>())!;
        Assert.NotEqual(first.Version, changed.Version);
        Assert.NotNull(changed.UpdatedAt);
        using var stale = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/departments/{first.Id}",
            new { Name = first.Name, Description = "Overwrite", first.Version });
        await AssertErrorAsync(stale, HttpStatusCode.Conflict, "VersionConflict");
        Assert.Equal("Updated", (await test.GetAsync(first.Id)).Description);
        using var missing = await test.Accounts.Admin.GetAsync($"/api/v1/departments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var deletion = await test.Accounts.Admin.DeleteAsync($"/api/v1/departments/{first.Id}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deletion.StatusCode);
    }

    [PostgresFact]
    public async Task NameIsReservedAcrossCaseSpacesAndInactiveDepartmentsAndFailedRenameIsAtomic()
    {
        await using var test = await Departments.CreateAsync(fixture.ConnectionString);
        var first = await test.CreateAsync("Finance");
        var second = await test.CreateAsync("Other");
        using var status = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/departments/{first.Id}/status",
            new { IsActive = false, first.Version });
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var duplicate = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/departments",
            new { Name = "  " + first.Name.ToUpperInvariant() + "  " });
        await AssertErrorAsync(duplicate, HttpStatusCode.Conflict, "NameInUse");
        await using var database = test.Accounts.Database();
        var before = await database.IdentityAuditLogs.CountAsync(x => x.ActorId == test.Accounts.AdminUser.Id);
        using var rename = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/departments/{second.Id}",
            new { Name = first.Name, Description = "secret-marker", second.Version });
        await AssertErrorAsync(rename, HttpStatusCode.Conflict, "NameInUse");
        Assert.DoesNotContain("secret-marker", await rename.Content.ReadAsStringAsync());
        Assert.Equal(second, await test.GetAsync(second.Id));
        Assert.Equal(before, await database.IdentityAuditLogs.CountAsync(x => x.ActorId == test.Accounts.AdminUser.Id));
    }

    [PostgresFact]
    public async Task ConcurrentCreateAndUpdateCannotDuplicateNamesOrOverwriteChanges()
    {
        await using var test = await Departments.CreateAsync(fixture.ConnectionString);
        var name = test.Prefix + "Concurrent";
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            test.Accounts.Admin.PostAsJsonAsync("/api/v1/departments", new { Name = name })));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            var created = await Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created)
                .Content.ReadFromJsonAsync<DepartmentDetails>();
            test.Own(created!.Id);
            var updates = await Task.WhenAll(Enumerable.Range(0, 2).Select(i =>
                test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/departments/{created.Id}",
                    new { created.Name, Description = $"Update {i}", created.Version })));
            try
            {
                Assert.Single(updates, x => x.StatusCode == HttpStatusCode.OK);
                Assert.Single(updates, x => x.StatusCode == HttpStatusCode.Conflict);
            }
            finally { foreach (var response in updates) response.Dispose(); }
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task DeactivatePreservesUserTeamAndSessionAndExistingAssignmentCanBeKeptOrCleared()
    {
        await using var test = await Departments.CreateAsync(fixture.ConnectionString);
        var department = await test.CreateAsync("History");
        var user = await test.Accounts.NewUserAsync();
        await using var database = test.Accounts.Database();
        var team = new SupportTeam { DepartmentId = department.Id, Name = "Historical team" };
        database.SupportTeams.Add(team);
        await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DepartmentId, department.Id));
        await database.SaveChangesAsync();
        using var userClient = await test.Accounts.ClientAsync(user);
        using var deactivate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/departments/{department.Id}/status",
            new { IsActive = false, department.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var inactive = (await deactivate.Content.ReadFromJsonAsync<DepartmentDetails>())!;
        Assert.False(inactive.IsActive);
        Assert.Equal(department.Id, (await test.Accounts.GetAsync(user.Id)).DepartmentId);
        Assert.True((await database.SupportTeams.AsNoTracking().SingleAsync(x => x.Id == team.Id)).IsActive);
        using var me = await userClient.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var current = await test.Accounts.GetAsync(user.Id);
        using var profile = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}",
            new { DisplayName = "Changed", DepartmentId = department.Id, current.Version });
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        current = (await profile.Content.ReadFromJsonAsync<AccountUser>())!;
        using var clear = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}",
            new { current.DisplayName, DepartmentId = (Guid?)null, current.Version });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        current = (await clear.Content.ReadFromJsonAsync<AccountUser>())!;
        using var reassign = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}",
            new { current.DisplayName, DepartmentId = department.Id, current.Version });
        await AssertErrorAsync(reassign, HttpStatusCode.BadRequest, "InvalidDepartment");
        using var lookupResponse = await test.Accounts.Admin.GetAsync($"/api/v1/departments/lookup?search={test.Prefix}");
        var lookup = await lookupResponse.Content.ReadFromJsonAsync<PagedResult<DepartmentLookup>>();
        Assert.Empty(lookup!.Items);
        using var reactivate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/departments/{department.Id}/status",
            new { IsActive = true, inactive.Version });
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        using var assign = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}",
            new { current.DisplayName, DepartmentId = department.Id, current.Version });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
    }

    [PostgresFact]
    public async Task AssignmentWaitsForConcurrentDeactivateAndRejectsInactiveDepartment()
    {
        await using var test = await Departments.CreateAsync(fixture.ConnectionString);
        var department = await test.CreateAsync("Race");
        var user = await test.Accounts.NewUserAsync();
        var current = await test.Accounts.GetAsync(user.Id);
        await using var database = test.Accounts.Database();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)");
        await database.Departments.Where(x => x.Id == department.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        var assignment = test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}",
            new { current.DisplayName, DepartmentId = department.Id, current.Version });
        await transaction.CommitAsync();
        using var response = await assignment;
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "InvalidDepartment");
        Assert.Null((await test.Accounts.GetAsync(user.Id)).DepartmentId);
    }

    [PostgresFact]
    public async Task PermissionsAreSeparateAndChangesAreReadOnNextRequest()
    {
        await using var test = await Departments.CreateAsync(fixture.ConnectionString);
        var department = await test.CreateAsync("Authorization");
        using var anonymous = test.Accounts.Factory.CreateClient();
        using var denied = await anonymous.GetAsync("/api/v1/departments");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var userManager = await test.Accounts.NewUserAsync();
        await test.Accounts.AssignAsync(userManager.Id, await test.Accounts.CustomRoleAsync(PermissionCodes.UserManage));
        using var users = await test.Accounts.ClientAsync(userManager);
        using var lookup = await users.GetAsync("/api/v1/departments/lookup");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        foreach (var route in new[] { "/api/v1/departments", $"/api/v1/departments/{department.Id}" })
        {
            using var forbidden = await users.GetAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using var create = await users.PostAsJsonAsync("/api/v1/departments", new { Name = "Forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        var manager = await test.Accounts.NewUserAsync();
        var role = await test.Accounts.CustomRoleAsync(PermissionCodes.DepartmentManage);
        await test.Accounts.AssignAsync(manager.Id, role);
        using var client = await test.Accounts.ClientAsync(manager);
        using var list = await client.GetAsync("/api/v1/departments");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var cannotAssign = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, cannotAssign.StatusCode);
        using var cannotGrant = await client.GetAsync("/api/v1/identity/roles");
        Assert.Equal(HttpStatusCode.Forbidden, cannotGrant.StatusCode);
        await using var database = test.Accounts.Database();
        await database.RolePermissions.Where(x => x.RoleId == role).ExecuteDeleteAsync();
        using var revoked = await client.PutAsJsonAsync($"/api/v1/departments/{department.Id}/status",
            new { IsActive = false, department.Version });
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.True((await test.GetAsync(department.Id)).IsActive);
        using var requester = await test.Accounts.ClientAsync(await test.Accounts.NewUserAsync());
        using var noLookup = await requester.GetAsync("/api/v1/departments/lookup");
        Assert.Equal(HttpStatusCode.Forbidden, noLookup.StatusCode);
    }

    [PostgresTheory]
    [InlineData("grant")]
    [InlineData("session")]
    [InlineData("inactive")]
    public async Task MutationRechecksActorInsideTransaction(string change)
    {
        await using var test = await Departments.CreateAsync(fixture.ConnectionString);
        var manager = await test.Accounts.NewUserAsync();
        var role = await test.Accounts.CustomRoleAsync(PermissionCodes.DepartmentManage);
        await test.Accounts.AssignAsync(manager.Id, role);
        var login = await test.Accounts.LoginAsync(manager);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var actor = new AccountActor(manager.Id, Guid.Parse(token.Claims.Single(x => x.Type == "sid").Value));
        await using var database = test.Accounts.Database();
        if (change == "grant") await database.RolePermissions.Where(x => x.RoleId == role).ExecuteDeleteAsync();
        else if (change == "session") await database.RefreshTokens.Where(x => x.UserId == manager.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        else await database.Users.Where(x => x.Id == manager.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DepartmentService>();
        var error = await Assert.ThrowsAsync<DepartmentOperationException>(() => service.CreateAsync(actor,
            new(test.Prefix + "Forbidden", null), CancellationToken.None));
        Assert.Equal(change == "grant" ? DepartmentError.InsufficientScope : DepartmentError.Unauthenticated, error.Error);
        Assert.False(await database.Departments.AnyAsync(x => x.Name == test.Prefix + "Forbidden"));
    }

    [PostgresTheory]
    [InlineData("blank")]
    [InlineData("long-name")]
    [InlineData("long-description")]
    [InlineData("overposting")]
    [InlineData("version-missing")]
    [InlineData("status-missing")]
    [InlineData("page-zero")]
    [InlineData("page-size")]
    [InlineData("sort")]
    public async Task InvalidRequestsReturnSanitizedBadRequest(string scenario)
    {
        await using var test = await Departments.CreateAsync(fixture.ConnectionString);
        var department = await test.CreateAsync("Validation");
        using var response = scenario switch
        {
            "blank" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/departments", new { Name = "   " }),
            "long-name" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/departments", new { Name = new string('x', 201) }),
            "long-description" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/departments", new { Name = "Test", Description = new string('x', 2001) }),
            "overposting" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/departments", new { Name = "Test", IsActive = false }),
            "version-missing" => await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/departments/{department.Id}", new { Name = department.Name }),
            "status-missing" => await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/departments/{department.Id}/status", new { department.Version }),
            "page-zero" => await test.Accounts.Admin.GetAsync("/api/v1/departments?page=0"),
            "page-size" => await test.Accounts.Admin.GetAsync("/api/v1/departments/lookup?pageSize=101"),
            _ => await test.Accounts.Admin.GetAsync("/api/v1/departments?sortBy=description")
        };
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [PostgresFact]
    public async Task SeedPreservesExistingGrantsAndExplicitDepartmentGrantIsIdempotentAndAudited()
    {
        await using var test = await Departments.CreateAsync(fixture.ConnectionString);
        var roleId = await test.Accounts.RoleIdAsync(RoleNames.SystemAdmin);
        await test.Accounts.SnapshotAsync(roleId);
        await using var database = test.Accounts.Database();
        var permissionId = await database.Permissions.Where(x => x.Code == PermissionCodes.DepartmentManage).Select(x => x.Id).SingleAsync();
        var previousAudits = await database.IdentityAuditLogs.Where(x => x.Action == "DepartmentAdministrationEnabled").Select(x => x.Id).ToArrayAsync();
        try
        {
            await database.RolePermissions.Where(x => x.RoleId == roleId && x.PermissionId == permissionId).ExecuteDeleteAsync();
            var initializer = new IdentityInitializer(database, TestAccounts.Hasher());
            await initializer.SeedAsync();
            Assert.False(await database.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
            await initializer.GrantDepartmentAdministrationAsync();
            await initializer.GrantDepartmentAdministrationAsync();
            Assert.True(await database.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
            Assert.Equal(previousAudits.Length + 1, await database.IdentityAuditLogs.CountAsync(x => x.Action == "DepartmentAdministrationEnabled"));
        }
        finally
        {
            await database.IdentityAuditLogs.Where(x => x.Action == "DepartmentAdministrationEnabled" && !previousAudits.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    private sealed class Departments(TestAccounts accounts) : IAsyncDisposable
    {
        private readonly List<Guid> _ids = [];
        public string Prefix { get; } = Guid.NewGuid().ToString("N") + "-";
        public TestAccounts Accounts { get; } = accounts;
        public static async Task<Departments> CreateAsync(string connection) => new(await TestAccounts.CreateAsync(connection));
        public void Own(Guid id) => _ids.Add(id);
        public async Task<DepartmentDetails> CreateAsync(string name, string? description = null)
        {
            using var response = await Accounts.Admin.PostAsJsonAsync("/api/v1/departments", new { Name = " " + Prefix + name + " ", Description = description });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var department = (await response.Content.ReadFromJsonAsync<DepartmentDetails>())!;
            Own(department.Id);
            Assert.EndsWith($"/api/v1/departments/{department.Id}", response.Headers.Location?.ToString());
            return department;
        }
        public async Task<DepartmentDetails> GetAsync(Guid id) =>
            (await Accounts.Admin.GetFromJsonAsync<DepartmentDetails>($"/api/v1/departments/{id}"))!;
        public async ValueTask DisposeAsync()
        {
            await using var database = Accounts.Database();
            await database.SupportTeams.Where(x => _ids.Contains(x.DepartmentId)).ExecuteDeleteAsync();
            await Accounts.DisposeAsync(); // Deletes only users/audit owned by this test.
            await database.Departments.Where(x => _ids.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }
}
