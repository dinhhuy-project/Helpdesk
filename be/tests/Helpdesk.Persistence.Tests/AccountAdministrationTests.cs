using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Auth;
using be.Contracts.Responses;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Authentication;
using be.Infrastructure.Identity;
using be.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class AccountAdministrationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task CreateListDetailUpdateHaveSafeDtosAndOptimisticConcurrency()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var email = $"{Guid.NewGuid():N}@example.test";
        var password = Guid.NewGuid().ToString("N");
        using var created = await test.Admin.PostAsJsonAsync("/api/v1/users", new { Email = email.ToUpperInvariant(), DisplayName = "New requester", Password = password });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var account = await ReadAsync<AccountUser>(created);
        test.Own(account.Id);
        Assert.Equal(email, account.Email);
        Assert.Equal(new[] { RoleNames.Requester }, account.Roles.Select(x => x.Name));
        Assert.True(account.Version > 0);
        var body = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        var login = await test.LoginAsync(new(account.Id, email, password));
        Assert.Equal(account.Id, login.User.Id);
        var list = await test.Admin.GetFromJsonAsync<PagedResult<AccountUser>>($"/api/v1/users?search={email}&pageSize=1&sortBy=email&descending=false");
        Assert.NotNull(list);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(account.Id, Assert.Single(list.Items).Id);
        using var updated = await test.Admin.PatchAsJsonAsync($"/api/v1/users/{account.Id}", new { DisplayName = "Changed", DepartmentId = (Guid?)null, account.Version });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var current = await ReadAsync<AccountUser>(updated);
        Assert.NotEqual(account.Version, current.Version);
        using var stale = await test.Admin.PatchAsJsonAsync($"/api/v1/users/{account.Id}", new { DisplayName = "Stale", account.Version });
        await AssertErrorAsync(stale, HttpStatusCode.Conflict, "VersionConflict");
        Assert.Equal("Changed", (await test.GetAsync(account.Id)).DisplayName);
        using var missing = await test.Admin.GetAsync($"/api/v1/users/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [PostgresFact]
    public async Task DuplicateNormalizedEmailsIncludingConcurrentCreationAreRejected()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var email = $"{Guid.NewGuid():N}@example.test";
        var password = Guid.NewGuid().ToString("N");
        var responses = await Task.WhenAll(
            test.Admin.PostAsJsonAsync("/api/v1/users", new { Email = email, DisplayName = "First", Password = password }),
            test.Admin.PostAsJsonAsync("/api/v1/users", new { Email = email.ToUpperInvariant(), DisplayName = "Second", Password = password }));
        try
        {
            var winner = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
            test.Own((await ReadAsync<AccountUser>(winner)).Id);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            await using var database = test.Database();
            Assert.Equal(1, await database.Users.CountAsync(x => x.NormalizedEmail == email));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresTheory]
    [InlineData("create-role")]
    [InlineData("create-active")]
    [InlineData("update-password")]
    [InlineData("status-missing")]
    [InlineData("roles-missing")]
    [InlineData("page-zero")]
    [InlineData("page-too-large")]
    [InlineData("sort-injection")]
    [InlineData("missing-version")]
    [InlineData("roles-duplicate")]
    [InlineData("roles-unknown")]
    [InlineData("grants-duplicate")]
    [InlineData("short-password")]
    public async Task InvalidInputAndOverpostingAreRejected(string failure)
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var user = await test.NewUserAsync();
        var current = await test.GetAsync(user.Id);
        using var response = failure switch
        {
            "create-role" => await test.Admin.PostAsJsonAsync("/api/v1/users", new { Email = user.Email, DisplayName = "Name", Password = "not-a-real-password", RoleIds = new[] { Guid.NewGuid() } }),
            "create-active" => await test.Admin.PostAsJsonAsync("/api/v1/users", new { Email = user.Email, DisplayName = "Name", Password = "not-a-real-password", IsActive = false }),
            "update-password" => await test.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}", new { DisplayName = "Name", current.Version, Password = "secret-marker" }),
            "status-missing" => await test.Admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/status", new { current.Version }),
            "roles-missing" => await test.Admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/roles", new { current.Version }),
            "page-zero" => await test.Admin.GetAsync("/api/v1/users?page=0"),
            "page-too-large" => await test.Admin.GetAsync("/api/v1/users?pageSize=101"),
            "missing-version" => await test.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}", new { DisplayName = "Name" }),
            "roles-duplicate" => await test.Admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/roles", new { current.Version, RoleIds = new[] { user.Id, user.Id } }),
            "roles-unknown" => await test.Admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/roles", new { current.Version, RoleIds = new[] { Guid.NewGuid() } }),
            "grants-duplicate" => await test.Admin.PutAsJsonAsync($"/api/v1/identity/roles/{Guid.NewGuid()}/permissions", new { current.Version, PermissionIds = new[] { user.Id, user.Id } }),
            "short-password" => await test.Admin.PostAsJsonAsync("/api/v1/users", new { user.Email, DisplayName = "Name", Password = "short" }),
            _ => await test.Admin.GetAsync("/api/v1/users?sortBy=passwordHash")
        };
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("secret-marker", await response.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task UserManagementCannotAssignRolesOrModifyPrivilegedUsers()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var delegated = await test.NewUserAsync();
        var role = await test.CustomRoleAsync(PermissionCodes.UserManage);
        await test.AssignAsync(delegated.Id, role);
        using var client = await test.ClientAsync(delegated);
        using var list = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var target = await test.NewUserAsync();
        var current = await test.GetAsync(target.Id);
        using var roles = await client.PutAsJsonAsync($"/api/v1/users/{target.Id}/roles", new { RoleIds = new[] { role }, current.Version });
        Assert.Equal(HttpStatusCode.Forbidden, roles.StatusCode);
        using var grants = await client.GetAsync("/api/v1/identity/permissions");
        Assert.Equal(HttpStatusCode.Forbidden, grants.StatusCode);
        var admin = await test.GetAsync(test.AdminUser.Id);
        using var status = await client.PutAsJsonAsync($"/api/v1/users/{admin.Id}/status", new { IsActive = false, admin.Version });
        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        using var profile = await client.PatchAsJsonAsync($"/api/v1/users/{admin.Id}", new { DisplayName = "Hijacked", admin.Version });
        Assert.Equal(HttpStatusCode.Forbidden, profile.StatusCode);
    }

    [PostgresFact]
    public async Task DepartmentUpdatesRequireAnActiveDepartmentAndCanBeCleared()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var user = await test.NewUserAsync();
        await using var database = test.Database();
        var department = new Department { Name = "Account test department", IsActive = false };
        database.Departments.Add(department);
        await database.SaveChangesAsync();
        try
        {
            var current = await test.GetAsync(user.Id);
            foreach (var id in new[] { Guid.NewGuid(), department.Id })
            {
                using var invalid = await test.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}",
                    new { current.DisplayName, current.Version, DepartmentId = id });
                await AssertErrorAsync(invalid, HttpStatusCode.BadRequest, "InvalidDepartment");
            }
            department.IsActive = true;
            await database.SaveChangesAsync();
            using var accepted = await test.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}",
                new { current.DisplayName, current.Version, DepartmentId = department.Id });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            current = await ReadAsync<AccountUser>(accepted);
            Assert.Equal(department.Id, current.DepartmentId);
            using var cleared = await test.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}",
                new { current.DisplayName, current.Version, DepartmentId = (Guid?)null });
            Assert.Null((await ReadAsync<AccountUser>(cleared)).DepartmentId);
        }
        finally
        {
            await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DepartmentId, (Guid?)null));
            await database.Departments.Where(x => x.Id == department.Id).ExecuteDeleteAsync();
        }
    }

    [PostgresFact]
    public async Task AccountEndpointsRequireAuthenticationAndManagementPermission()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        using var anonymous = test.Factory.CreateClient();
        using var unauthenticated = await anonymous.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        var user = await test.NewUserAsync();
        using var requester = await test.ClientAsync(user);
        using var denied = await requester.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var me = await requester.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [PostgresFact]
    public async Task DelegatedRoleManagementIsLimitedToOwnGrantsAndCannotEditSelf()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var delegated = await test.NewUserAsync();
        await test.AssignAsync(delegated.Id, await test.CustomRoleAsync(PermissionCodes.RoleManage,
            PermissionCodes.TicketCreate, PermissionCodes.TicketReadOwn, PermissionCodes.TicketCommentPublic,
            PermissionCodes.TicketConfirm, PermissionCodes.TicketReopen, PermissionCodes.TicketCancel));
        using var client = await test.ClientAsync(delegated);
        var target = await test.NewUserAsync();
        var current = await test.GetAsync(target.Id);
        using var allowed = await client.PutAsJsonAsync($"/api/v1/users/{target.Id}/roles",
            new { RoleIds = new[] { await test.RoleIdAsync(RoleNames.Requester) }, current.Version });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        current = await ReadAsync<AccountUser>(allowed);
        foreach (var name in new[] { RoleNames.TeamLeader, RoleNames.SystemAdmin })
        {
            using var denied = await client.PutAsJsonAsync($"/api/v1/users/{target.Id}/roles",
                new { RoleIds = new[] { await test.RoleIdAsync(name) }, current.Version });
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        var own = await test.GetAsync(delegated.Id);
        using var self = await client.PutAsJsonAsync($"/api/v1/users/{own.Id}/roles", new { RoleIds = Array.Empty<Guid>(), own.Version });
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        var role = (await test.RolesAsync()).Single(x => x.Name == RoleNames.Requester);
        using var editGrant = await client.PutAsJsonAsync($"/api/v1/identity/roles/{role.Id}/permissions",
            new { PermissionIds = Array.Empty<Guid>(), role.Version });
        Assert.Equal(HttpStatusCode.Forbidden, editGrant.StatusCode);
        using var audit = await client.GetAsync("/api/v1/identity/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, audit.StatusCode);
    }

    [PostgresFact]
    public async Task RoleAndGrantChangesUpdateVersionsAndProtectAdministrativeGrants()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var target = await test.NewUserAsync();
        var login = await test.LoginAsync(target);
        var current = await test.GetAsync(target.Id);
        var roleId = await test.CustomRoleAsync(PermissionCodes.TicketCreate);
        using var assigned = await test.Admin.PutAsJsonAsync($"/api/v1/users/{target.Id}/roles", new { RoleIds = new[] { roleId }, current.Version });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        using var stale = await test.Admin.PutAsJsonAsync($"/api/v1/users/{target.Id}/roles", new { RoleIds = Array.Empty<Guid>(), current.Version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var role = (await test.RolesAsync()).Single(x => x.Id == roleId);
        var permissions = await test.Admin.GetFromJsonAsync<AccountPermission[]>("/api/v1/identity/permissions");
        Assert.NotNull(permissions);
        using var changed = await test.Admin.PutAsJsonAsync($"/api/v1/identity/roles/{roleId}/permissions",
            new { PermissionIds = new[] { permissions.Single(x => x.Code == PermissionCodes.UserManage).Id }, role.Version });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(role.Version, (await ReadAsync<AccountRoleDetails>(changed)).Version);
        using var targetClient = test.Factory.CreateClient();
        targetClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var userList = await targetClient.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.OK, userList.StatusCode); // Existing JWT gains the current grant.
        using var old = await test.Admin.PutAsJsonAsync($"/api/v1/identity/roles/{roleId}/permissions",
            new { PermissionIds = Array.Empty<Guid>(), role.Version });
        Assert.Equal(HttpStatusCode.Conflict, old.StatusCode);
        var adminRole = (await test.RolesAsync()).Single(x => x.Name == RoleNames.SystemAdmin);
        using var protectedGrant = await test.Admin.PutAsJsonAsync($"/api/v1/identity/roles/{adminRole.Id}/permissions",
            new { PermissionIds = permissions.Where(x => x.Code != PermissionCodes.RoleManage).Select(x => x.Id).ToArray(), adminRole.Version });
        await AssertErrorAsync(protectedGrant, HttpStatusCode.Conflict, "ProtectedAdminGrant");
    }

    [PostgresFact]
    public async Task DeactivationRevokesAllSessionsAndReactivationDoesNotReviveThem()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var user = await test.NewUserAsync();
        var first = await test.LoginAsync(user);
        var second = await test.LoginAsync(user);
        var current = await test.GetAsync(user.Id);
        using var disabled = await test.Admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/status", new { IsActive = false, current.Version });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        current = await ReadAsync<AccountUser>(disabled);
        using var enabled = await test.Admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/status", new { IsActive = true, current.Version });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        foreach (var login in new[] { first, second })
        {
            using var client = test.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            using var me = await client.GetAsync("/api/v1/users/me");
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
            using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { login.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }
        await test.LoginAsync(user);
    }

    [PostgresFact]
    public async Task ConcurrentSelfDeactivationsCannotRemoveTheLastActiveAdmin()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        await test.SuspendOtherAdminsAsync();
        var second = await test.NewUserAsync();
        await test.AssignAsync(second.Id, await test.RoleIdAsync(RoleNames.SystemAdmin));
        using var secondClient = await test.ClientAsync(second);
        var firstVersion = (await test.GetAsync(test.AdminUser.Id)).Version;
        var secondVersion = (await test.GetAsync(second.Id)).Version;
        var responses = await Task.WhenAll(
            test.Admin.PutAsJsonAsync($"/api/v1/users/{test.AdminUser.Id}/status", new { IsActive = false, Version = firstVersion }),
            secondClient.PutAsJsonAsync($"/api/v1/users/{second.Id}/status", new { IsActive = false, Version = secondVersion }));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            var denied = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            await AssertErrorAsync(denied, HttpStatusCode.Conflict, "LastActiveAdmin");
            await using var database = test.Database();
            Assert.Equal(1, await database.Users.CountAsync(x => x.IsActive && (x.Id == second.Id || x.Id == test.AdminUser.Id)));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task ConcurrentUpdatesWithTheSameVersionHaveOnlyOneWinner()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var user = await test.NewUserAsync();
        var current = await test.GetAsync(user.Id);
        var responses = await Task.WhenAll(new[] { "First", "Second" }.Select(name =>
            test.Admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}", new { DisplayName = name, current.Version })));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task OrdinaryUsersCanChangeOwnPasswordAndAllOldCredentialsAreInvalidated()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var user = await test.NewUserAsync();
        var second = await test.LoginAsync(user);
        using var client = await test.ClientAsync(user);
        using var invalid = await client.PostAsJsonAsync("/api/v1/users/me/password", new { CurrentPassword = "wrong-password", NewPassword = "a-valid-new-password" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var password = Guid.NewGuid().ToString("N");
        using var changed = await client.PostAsJsonAsync("/api/v1/users/me/password", new { CurrentPassword = user.Password, NewPassword = password });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using var me = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var refresh = await test.Admin.PostAsJsonAsync("/api/v1/auth/refresh", new { second.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using var oldLogin = await test.Admin.PostAsJsonAsync("/api/v1/auth/login", new { user.Email, user.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        await test.LoginAsync(user with { Password = password });
        await using var database = test.Database();
        var audits = await database.IdentityAuditLogs.Where(x => x.TargetUserId == user.Id).ToListAsync();
        Assert.Single(audits, x => x.Action == "PasswordChanged");
        Assert.DoesNotContain(password, JsonSerializer.Serialize(audits));
        Assert.DoesNotContain(user.Password, JsonSerializer.Serialize(audits));
    }

    [PostgresFact]
    public async Task PasswordChangeCannotBeBypassedByAnInFlightOldPasswordLogin()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var user = await test.NewUserAsync();
        using var client = await test.ClientAsync(user);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var paused = test.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordVerifier>();
            services.AddScoped<IPasswordVerifier>(provider => new PausedVerifier(new PasswordVerifier(
                provider.GetRequiredService<IPasswordHasher<User>>(), provider.GetRequiredService<DummyPasswordCredential>()), user.Id, reached, release));
        }));
        using var loginClient = paused.CreateClient();
        var pending = loginClient.PostAsJsonAsync("/api/v1/auth/login", new { user.Email, user.Password });
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var changed = await client.PostAsJsonAsync("/api/v1/users/me/password",
                new { CurrentPassword = user.Password, NewPassword = Guid.NewGuid().ToString("N") });
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        }
        finally { release.Set(); }
        using var stale = await pending;
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        await using var database = test.Database();
        Assert.False(await database.RefreshTokens.AnyAsync(x => x.UserId == user.Id && x.RevokedAt == null));
    }

    [PostgresTheory]
    [InlineData("permission")]
    [InlineData("session")]
    public async Task MutationsRecheckActorAuthorityInsideTheTransaction(string failure)
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var user = await test.NewUserAsync();
        var current = await test.GetAsync(user.Id);
        var login = await test.LoginAsync(test.AdminUser);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var actor = new AccountActor(test.AdminUser.Id, Guid.Parse(jwt.Claims.Single(x => x.Type == "sid").Value));
        await using var database = test.Database();
        if (failure == "permission")
        {
            var role = await test.RoleIdAsync(RoleNames.SystemAdmin);
            await test.SnapshotAsync(role);
            var permissionId = await database.Permissions.Where(x => x.Code == PermissionCodes.UserManage).Select(x => x.Id).SingleAsync();
            await database.RolePermissions.Where(x => x.RoleId == role && x.PermissionId == permissionId).ExecuteDeleteAsync();
        }
        else await database.RefreshTokens.Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        await using var scope = test.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        var error = await Assert.ThrowsAsync<AccountOperationException>(() => service.SetStatusAsync(actor, user.Id, new(false, current.Version), CancellationToken.None));
        Assert.Equal(failure == "permission" ? AccountError.InsufficientScope : AccountError.Unauthenticated, error.Error);
        Assert.True(await database.Users.Where(x => x.Id == user.Id).Select(x => x.IsActive).SingleAsync());
    }

    [PostgresFact]
    public async Task AuditEndpointIsAdministrativeAndGrantCliIsIdempotent()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var roleId = await test.RoleIdAsync(RoleNames.SystemAdmin);
        await test.SnapshotAsync(roleId);
        await using var database = test.Database();
        var grantId = await database.Permissions.Where(x => x.Code == PermissionCodes.RoleManage).Select(x => x.Id).SingleAsync();
        await database.RolePermissions.Where(x => x.RoleId == roleId && x.PermissionId == grantId).ExecuteDeleteAsync();
        await new IdentityInitializer(database, TestAccounts.Hasher()).SeedAsync();
        Assert.False(await database.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == grantId));
        var initializer = new IdentityInitializer(database, TestAccounts.Hasher());
        var before = await database.IdentityAuditLogs.CountAsync(x => x.Action == "AccountAdministrationEnabled");
        await initializer.GrantAccountAdministrationAsync();
        await initializer.GrantAccountAdministrationAsync();
        Assert.Equal(before + 1, await database.IdentityAuditLogs.CountAsync(x => x.Action == "AccountAdministrationEnabled"));
        using var audit = await test.Admin.GetAsync("/api/v1/identity/audit-logs?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        var page = await ReadAsync<PagedResult<AccountAudit>>(audit);
        Assert.True(page.TotalCount > 0);
        Assert.Single(page.Items);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(result);
        return result;
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    private sealed class PausedVerifier(IPasswordVerifier inner, Guid id, TaskCompletionSource reached, ManualResetEventSlim release) : IPasswordVerifier
    {
        public PasswordCheck Verify(User? user, string password)
        {
            var result = inner.Verify(user, password);
            if (user?.Id == id && result.Succeeded)
            {
                reached.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Test coordination timed out.");
            }
            return result;
        }
    }

    internal sealed record TestUser(Guid Id, string Email, string Password);

    internal sealed class TestAccounts(string connection) : IAsyncDisposable
    {
        private readonly List<Guid> _users = [];
        private readonly List<Guid> _roles = [];
        private readonly Dictionary<Guid, Guid[]> _snapshots = [];
        private readonly List<Guid> _suspended = [];
        public WebApplicationFactory<be.Program> Factory { get; private set; } = null!;
        public HttpClient Admin { get; private set; } = null!;
        public TestUser AdminUser { get; private set; } = null!;
        public HelpdeskDbContext Database() => ModelTests.CreateContext(connection);

        public static async Task<TestAccounts> CreateAsync(string connection)
        {
            var test = new TestAccounts(connection);
            await using var database = test.Database();
            await new IdentityInitializer(database, Hasher()).SeedAsync();
            test.Factory = new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Helpdesk"] = connection, ["AuthRateLimit:PermitLimit"] = "1000" })));
            test.AdminUser = await test.NewUserAsync();
            await test.AssignAsync(test.AdminUser.Id, await test.RoleIdAsync(RoleNames.SystemAdmin));
            test.Admin = await test.ClientAsync(test.AdminUser);
            return test;
        }

        public static PasswordHasher<User> Hasher() => new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
        public void Own(Guid id) => _users.Add(id);

        public async Task<TestUser> NewUserAsync()
        {
            var password = Guid.NewGuid().ToString("N");
            var user = new User { Email = $"{Guid.NewGuid():N}@example.test", DisplayName = "Account test user" };
            user.PasswordHash = Hasher().HashPassword(user, password);
            await using var database = Database();
            database.Users.Add(user);
            await database.SaveChangesAsync();
            Own(user.Id);
            return new(user.Id, user.Email, password);
        }

        public async Task<LoginResponse> LoginAsync(TestUser user)
        {
            using var client = Factory.CreateClient();
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { user.Email, user.Password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ReadAsync<LoginResponse>(response);
        }

        public async Task<HttpClient> ClientAsync(TestUser user)
        {
            var login = await LoginAsync(user);
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            return client;
        }

        public async Task<AccountUser> GetAsync(Guid id) => (await Admin.GetFromJsonAsync<AccountUser>($"/api/v1/users/{id}"))!;
        public async Task<AccountRoleDetails[]> RolesAsync() => (await Admin.GetFromJsonAsync<AccountRoleDetails[]>("/api/v1/identity/roles"))!;
        public async Task<Guid> RoleIdAsync(string name)
        {
            await using var database = Database();
            return await database.Roles.Where(x => x.Name == name).Select(x => x.Id).SingleAsync();
        }

        public async Task AssignAsync(Guid userId, Guid roleId)
        {
            await using var database = Database();
            database.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
            await database.SaveChangesAsync();
        }

        public async Task<Guid> CustomRoleAsync(params string[] permissions)
        {
            await using var database = Database();
            var role = new Role { Name = $"Test-{Guid.NewGuid():N}" };
            database.Roles.Add(role);
            var ids = await database.Permissions.Where(x => permissions.Contains(x.Code)).Select(x => x.Id).ToArrayAsync();
            database.RolePermissions.AddRange(ids.Select(id => new RolePermission { RoleId = role.Id, PermissionId = id }));
            await database.SaveChangesAsync();
            _roles.Add(role.Id);
            return role.Id;
        }

        public async Task SnapshotAsync(Guid roleId)
        {
            if (_snapshots.ContainsKey(roleId)) return;
            await using var database = Database();
            _snapshots[roleId] = await database.RolePermissions.Where(x => x.RoleId == roleId).Select(x => x.PermissionId).ToArrayAsync();
        }

        public async Task SuspendOtherAdminsAsync()
        {
            await using var database = Database();
            var adminId = await RoleIdAsync(RoleNames.SystemAdmin);
            _suspended.AddRange(await database.UserRoles.Where(x => x.RoleId == adminId && x.UserId != AdminUser.Id)
                .Join(database.Users.Where(x => x.IsActive), x => x.UserId, x => x.Id, (_, user) => user.Id).ToListAsync());
            await database.Users.Where(x => _suspended.Contains(x.Id)).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, false));
        }

        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            await Factory.DisposeAsync();
            await using var database = Database();
            foreach (var (roleId, grants) in _snapshots)
            {
                await database.RolePermissions.Where(x => x.RoleId == roleId).ExecuteDeleteAsync();
                database.RolePermissions.AddRange(grants.Select(id => new RolePermission { RoleId = roleId, PermissionId = id }));
                await database.SaveChangesAsync();
            }
            await database.Users.Where(x => _suspended.Contains(x.Id)).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, true));
            await database.Notifications.Where(x => _users.Contains(x.UserId)).ExecuteDeleteAsync();
            await database.IdentityAuditLogs.Where(x => (x.ActorId != null && _users.Contains(x.ActorId.Value)) ||
                (x.TargetUserId != null && _users.Contains(x.TargetUserId.Value)) || (x.TargetRoleId != null && _roles.Contains(x.TargetRoleId.Value))).ExecuteDeleteAsync();
            await database.RefreshTokens.Where(x => _users.Contains(x.UserId)).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReplacedByTokenId, (Guid?)null));
            await database.RefreshTokens.Where(x => _users.Contains(x.UserId)).ExecuteDeleteAsync();
            await database.UserRoles.Where(x => _users.Contains(x.UserId)).ExecuteDeleteAsync();
            await database.RolePermissions.Where(x => _roles.Contains(x.RoleId)).ExecuteDeleteAsync();
            await database.Roles.Where(x => _roles.Contains(x.Id)).ExecuteDeleteAsync();
            await database.Users.Where(x => _users.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }
}
