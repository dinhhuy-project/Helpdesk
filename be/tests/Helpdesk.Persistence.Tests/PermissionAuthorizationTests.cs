using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using be.Api.Authorization;
using be.Application.Common.Authorization;
using be.Contracts.Responses;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class PermissionAuthorizationTests(PostgresFixture fixture)
{
    [PostgresTheory]
    [InlineData(RoleNames.Requester)]
    [InlineData(RoleNames.SupportAgent)]
    [InlineData(RoleNames.TeamLeader)]
    [InlineData(RoleNames.SystemAdmin)]
    public async Task RoleDefaultsAreEnforcedAcrossAllPermissionPolicies(string role)
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        try
        {
            await AssignRoleAsync(user.Id, role);
            var login = await LoginAsync(client, user, password);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            var expected = ExpectedPermissions(role);
            foreach (var permission in PermissionCodes.All)
            {
                using var response = await client.GetAsync($"/__tests/permissions/check/{permission}");
                Assert.Equal(expected.Contains(permission) ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, response.StatusCode);
            }
            using var attribute = await client.GetAsync("/__tests/permissions/manage-users");
            Assert.Equal(role == RoleNames.SystemAdmin ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, attribute.StatusCode);
        }
        finally { await RemoveRolesAsync(user.Id); }
    }

    [PostgresFact]
    public async Task AddingAndRemovingRoleMembershipAffectsTheSameAccessToken()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var denied = await client.GetAsync("/__tests/permissions/create-ticket");
        await AssertForbiddenAsync(denied);
        using var me = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode); // No management permission is needed for one's own profile.
        await AssignRoleAsync(user.Id, RoleNames.Requester);
        using var granted = await client.GetAsync("/__tests/permissions/create-ticket");
        Assert.Equal(HttpStatusCode.NoContent, granted.StatusCode);
        await RemoveRolesAsync(user.Id);
        using var removed = await client.GetAsync("/__tests/permissions/create-ticket");
        await AssertForbiddenAsync(removed);
    }

    [PostgresFact]
    public async Task ChangingRoleGrantsAffectsAuthorizationAndMeWithoutLoginOrRefresh()
    {
        var (user, password) = await CreateUserAsync();
        await AssignRoleAsync(user.Id, RoleNames.Requester);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        var roleId = await database.Roles.Where(x => x.Name == RoleNames.Requester).Select(x => x.Id).SingleAsync();
        var permissionId = await database.Permissions.Where(x => x.Code == PermissionCodes.UserManage).Select(x => x.Id).SingleAsync();
        try
        {
            using var before = await client.GetAsync("/__tests/permissions/manage-users");
            await AssertForbiddenAsync(before);
            database.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
            await database.SaveChangesAsync();
            using var granted = await client.GetAsync("/__tests/permissions/manage-users");
            Assert.Equal(HttpStatusCode.NoContent, granted.StatusCode);
            var profile = await client.GetFromJsonAsync<UserResponse>("/api/v1/users/me");
            Assert.Contains(PermissionCodes.UserManage, profile!.Permissions);
            await database.RolePermissions.Where(x => x.RoleId == roleId && x.PermissionId == permissionId).ExecuteDeleteAsync();
            using var removed = await client.GetAsync("/__tests/permissions/manage-users");
            await AssertForbiddenAsync(removed);
            profile = await client.GetFromJsonAsync<UserResponse>("/api/v1/users/me");
            Assert.DoesNotContain(PermissionCodes.UserManage, profile!.Permissions);
        }
        finally
        {
            await database.RolePermissions.Where(x => x.RoleId == roleId && x.PermissionId == permissionId).ExecuteDeleteAsync();
        }
    }

    [PostgresFact]
    public async Task SystemAdminDoesNotBypassARevokedGrant()
    {
        var (user, password) = await CreateUserAsync();
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        var roleId = await database.Roles.Where(x => x.Name == RoleNames.SystemAdmin).Select(x => x.Id).SingleAsync();
        var permissionId = await database.Permissions.Where(x => x.Code == PermissionCodes.UserManage).Select(x => x.Id).SingleAsync();
        try
        {
            await AssignRoleAsync(user.Id, RoleNames.SystemAdmin);
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            var login = await LoginAsync(client, user, password);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            using var allowed = await client.GetAsync("/__tests/permissions/manage-users");
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
            await database.RolePermissions.Where(x => x.RoleId == roleId && x.PermissionId == permissionId).ExecuteDeleteAsync();
            using var denied = await client.GetAsync("/__tests/permissions/manage-users");
            await AssertForbiddenAsync(denied);
            var profile = await client.GetFromJsonAsync<UserResponse>("/api/v1/users/me");
            Assert.Contains(RoleNames.SystemAdmin, profile!.Roles);
            Assert.DoesNotContain(PermissionCodes.UserManage, profile.Permissions);
        }
        finally
        {
            if (!await database.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == permissionId))
            {
                database.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
                await database.SaveChangesAsync();
            }
            await RemoveRolesAsync(user.Id);
        }
    }

    [PostgresFact]
    public async Task MultipleRolesContributeGrantsAndClassAndMethodPoliciesAreBothRequired()
    {
        var (user, password) = await CreateUserAsync();
        await AssignRoleAsync(user.Id, RoleNames.Requester);
        await AssignRoleAsync(user.Id, RoleNames.SupportAgent);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var agent = await client.GetAsync("/__tests/team-permissions/assign");
        await AssertForbiddenAsync(agent); // Has class-level read.team, lacks method-level assign.
        await AssignRoleAsync(user.Id, RoleNames.TeamLeader);
        using var leader = await client.GetAsync("/__tests/team-permissions/assign");
        Assert.Equal(HttpStatusCode.NoContent, leader.StatusCode);
        await RemoveRolesAsync(user.Id);
        await AssignRoleAsync(user.Id, RoleNames.Requester);
        using var requester = await client.GetAsync("/__tests/team-permissions/assign");
        await AssertForbiddenAsync(requester);
    }

    [PostgresFact]
    public async Task PermissionAndAdminClaimsAndClientHeadersDoNotGrantDatabasePermissions()
    {
        var (user, password) = await CreateUserAsync();
        await AssignRoleAsync(user.Id, RoleNames.Requester);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        var original = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var claims = original.Claims.Where(x => x.Type is "sub" or "sid" or "iat" or "jti").ToList();
        claims.AddRange(new[]
        {
            new Claim("permission", PermissionCodes.UserManage), new Claim("permissions", PermissionCodes.UserManage),
            new Claim("role", RoleNames.SystemAdmin), new Claim(ClaimTypes.Role, RoleNames.SystemAdmin)
        });
        var token = new JwtSecurityToken(AuthTestSettings.Issuer, AuthTestSettings.Audience, claims, original.ValidFrom, original.ValidTo,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTestSettings.SigningKey)), SecurityAlgorithms.HmacSha256));
        token.Header["typ"] = "at+jwt";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        client.DefaultRequestHeaders.Add("X-Role", RoleNames.SystemAdmin);
        client.DefaultRequestHeaders.Add("X-Permissions", PermissionCodes.UserManage);
        client.DefaultRequestHeaders.Add("X-User-Id", Guid.NewGuid().ToString());
        using var authenticated = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode); // Signature/session are valid; authorization must still deny.
        using var denied = await client.GetAsync("/__tests/permissions/manage-users?role=SystemAdmin&permission=user.manage");
        await AssertForbiddenAsync(denied);
    }

    [PostgresTheory]
    [InlineData("inactive")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task InactiveAccountsOrInvalidSessionsReturn401BeforePermissionAccess(string failure)
    {
        var (user, password) = await CreateUserAsync();
        await AssignRoleAsync(user.Id, RoleNames.Requester);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var before = await client.GetAsync("/__tests/permissions/create-ticket");
        Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);
        await InvalidateSessionAsync(user.Id, failure);
        using var after = await client.GetAsync("/__tests/permissions/create-ticket");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Contains(after.Headers.WwwAuthenticate, x => x.Scheme == "Bearer");
    }

    [PostgresTheory]
    [InlineData("inactive")]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("wrong-owner")]
    [InlineData("unknown-session")]
    public async Task PermissionQueryRechecksAccountAndSessionEvenForImperativeCalls(string failure)
    {
        var (user, password) = await CreateUserAsync();
        await AssignRoleAsync(user.Id, RoleNames.Requester);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        var sessionId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken).Claims.Single(x => x.Type == "sid").Value);
        await using var scope = factory.Services.CreateAsyncScope();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        Assert.True(await checker.HasPermissionAsync(user.Id, sessionId, PermissionCodes.TicketCreate, CancellationToken.None));
        if (failure is "inactive" or "revoked" or "expired") await InvalidateSessionAsync(user.Id, failure);
        Assert.False(await checker.HasPermissionAsync(failure == "wrong-owner" ? Guid.NewGuid() : user.Id,
            failure == "unknown-session" ? Guid.NewGuid() : sessionId, PermissionCodes.TicketCreate, CancellationToken.None));
        Assert.False(await checker.HasPermissionAsync(user.Id, sessionId, "TICKET.CREATE", CancellationToken.None));
        Assert.False(await checker.HasPermissionAsync(user.Id, sessionId, "ticket.unknown", CancellationToken.None));
    }

    [PostgresFact]
    public async Task PermissionLookupFailureNeverAllowsTheControllerOrExposesDiagnostics()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPermissionChecker>();
            services.AddScoped<IPermissionChecker, FailingPermissionChecker>();
        }));
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var response = await client.GetAsync("/__tests/permissions/manage-users");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-marker", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.True(JsonDocument.Parse(body).RootElement.TryGetProperty("traceId", out _));
    }

    private WebApplicationFactory<be.Program> CreateFactory() =>
        new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Helpdesk"] = fixture.ConnectionString,
                ["AuthRateLimit:PermitLimit"] = 1000.ToString(CultureInfo.InvariantCulture)
            })));

    private async Task<(User User, string Password)> CreateUserAsync()
    {
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        var hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
        await new IdentityInitializer(database, hasher).SeedAsync();
        var password = Guid.NewGuid().ToString("N");
        var user = new User { Email = $"{Guid.NewGuid():N}@example.test", DisplayName = "Permission test user" };
        user.PasswordHash = hasher.HashPassword(user, password);
        database.Users.Add(user);
        await database.SaveChangesAsync();
        return (user, password);
    }

    private async Task AssignRoleAsync(Guid userId, string role)
    {
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        var roleId = await database.Roles.Where(x => x.Name == role).Select(x => x.Id).SingleAsync();
        database.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        await database.SaveChangesAsync();
    }

    private async Task RemoveRolesAsync(Guid userId)
    {
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        await database.UserRoles.Where(x => x.UserId == userId).ExecuteDeleteAsync();
    }

    private async Task InvalidateSessionAsync(Guid userId, string failure)
    {
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        if (failure == "inactive")
            await database.Users.Where(x => x.Id == userId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, false));
        else if (failure == "revoked")
            await database.RefreshTokens.Where(x => x.UserId == userId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        else
            await database.RefreshTokens.Where(x => x.UserId == userId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, User user, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { user.Email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        return result;
    }

    private static HashSet<string> ExpectedPermissions(string role)
    {
        if (role == RoleNames.SystemAdmin) return PermissionCodes.All.ToHashSet(StringComparer.Ordinal);
        var permissions = new HashSet<string>(StringComparer.Ordinal)
        {
            PermissionCodes.TicketCreate, PermissionCodes.TicketReadOwn, PermissionCodes.TicketCommentPublic,
            PermissionCodes.TicketConfirm, PermissionCodes.TicketReopen, PermissionCodes.TicketCancel
        };
        if (role is RoleNames.SupportAgent or RoleNames.TeamLeader)
            permissions.UnionWith(new[] { PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus, PermissionCodes.TicketCommentInternal });
        if (role == RoleNames.TeamLeader) permissions.UnionWith(new[] { PermissionCodes.TicketAssign, PermissionCodes.TeamManage });
        return permissions;
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(response.Headers.WwwAuthenticate);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        Assert.True(json.TryGetProperty("traceId", out _));
        Assert.Equal("Bạn không có quyền thực hiện thao tác này.", json.GetProperty("title").GetString());
        Assert.DoesNotContain("user.manage", body);
        Assert.DoesNotContain("Exception", body);
    }

    private sealed class FailingPermissionChecker : IPermissionChecker
    {
        public Task<bool> HasPermissionAsync(Guid userId, Guid sessionId, string permission, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("secret-marker");
    }
}

// These endpoints are discovered by ApiFactory's test assembly only, never by production MVC.
[ApiController]
[Authorize]
[Route("__tests/permissions")]
public class PermissionTestController(IAuthorizationService authorization) : ControllerBase
{
    [HttpGet("manage-users")]
    [RequirePermission(PermissionCodes.UserManage)]
    public IActionResult ManageUsers() => NoContent();

    [HttpGet("create-ticket")]
    [RequirePermission(PermissionCodes.TicketCreate)]
    public IActionResult CreateTicket() => NoContent();

    [HttpGet("check/{permission}")]
    public async Task<IActionResult> Check(string permission)
    {
        if (!PermissionCodes.All.Contains(permission, StringComparer.Ordinal)) return NotFound();
        var result = await authorization.AuthorizeAsync(User, HttpContext, permission);
        return result.Succeeded ? NoContent() : Forbid();
    }
}

[ApiController]
[RequirePermission(PermissionCodes.TicketReadTeam)]
[Route("__tests/team-permissions")]
public class TeamPermissionTestController : ControllerBase
{
    [HttpGet("assign")]
    [RequirePermission(PermissionCodes.TicketAssign)]
    public IActionResult Assign() => NoContent();
}
