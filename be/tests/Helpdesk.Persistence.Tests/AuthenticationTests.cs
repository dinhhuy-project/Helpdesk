using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using be.Contracts.Responses;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Helpdesk.Persistence.Tests;

internal static class AuthTestSettings
{
    public const string Issuer = "Helpdesk.Test.Api";
    public const string Audience = "Helpdesk.Test.Web";
    public const string SigningKey = "test-only-key-at-least-32-bytes-long-for-validation";
}

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class AuthenticationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task LoginAndMeReturnCurrentProfileWithoutSensitiveFields()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email.ToUpperInvariant(), password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.CacheControl?.NoStore);
        var result = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Contains(RoleNames.Requester, result.User.Roles);
        Assert.Contains(PermissionCodes.TicketCommentInternal, result.User.Permissions);
        Assert.Equal(result.User.Permissions.Count, result.User.Permissions.Distinct().Count());
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.Equal("HS256", token.Header.Alg);
        Assert.Equal("at+jwt", token.Header.Typ);
        Assert.Equal(AuthTestSettings.Issuer, token.Issuer);
        Assert.Contains(AuthTestSettings.Audience, token.Audiences);
        Assert.Equal(result.ExpiresAt, token.ValidTo);
        Assert.InRange(result.ExpiresAt, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
        Assert.Equal(user.Id.ToString(), token.Subject);
        Assert.DoesNotContain(token.Claims, x => x.Type is "role" or "permission" or "email");
        Assert.False(string.IsNullOrEmpty(token.Id));
        Assert.True(Guid.TryParse(token.Claims.Single(x => x.Type == "sid").Value, out _));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        using var me = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True(me.Headers.CacheControl?.NoStore);
        var body = await me.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, body);
        Assert.Equal(user.Id, (await me.Content.ReadFromJsonAsync<UserResponse>())?.Id);

        // Permission/role data is read afresh, rather than trusted from a login-time token snapshot.
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        var agentRoleId = await database.Roles.Where(x => x.Name == RoleNames.SupportAgent).Select(x => x.Id).SingleAsync();
        await database.UserRoles.Where(x => x.UserId == user.Id && x.RoleId == agentRoleId).ExecuteDeleteAsync();
        var updated = await client.GetFromJsonAsync<UserResponse>("/api/v1/users/me");
        Assert.NotNull(updated);
        Assert.Equal(new[] { RoleNames.Requester }, updated.Roles);
        Assert.DoesNotContain(PermissionCodes.TicketCommentInternal, updated.Permissions);
    }

    [PostgresFact]
    public async Task BadCredentialsUnknownInactiveAndPasswordlessAccountsHaveSameFailure()
    {
        var (user, password) = await CreateUserAsync();
        var (inactive, inactivePassword) = await CreateUserAsync(active: false);
        var (passwordless, _) = await CreateUserAsync(passwordless: true);
        var (corrupt, _) = await CreateUserAsync(corruptHash: true);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        string? expectedTitle = null;
        foreach (var attempt in new[]
        {
            (user.Email, "wrong-password"), ($"{Guid.NewGuid():N}@example.test", password),
            (inactive.Email, inactivePassword), (passwordless.Email, password), (corrupt.Email, password)
        })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = attempt.Item1, password = attempt.Item2 });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            expectedTitle ??= problem.GetProperty("title").GetString();
            Assert.Equal(expectedTitle, problem.GetProperty("title").GetString());
            Assert.True(problem.TryGetProperty("traceId", out _));
            Assert.False(problem.TryGetProperty("accessToken", out _));
        }
    }

    [PostgresFact]
    public async Task DisablingAUserInvalidatesTheirExistingAccessToken()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password });
        var result = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, false));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        using var me = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [PostgresTheory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("missing-sub")]
    [InlineData("invalid-sub")]
    [InlineData("missing-jti")]
    [InlineData("missing-iat")]
    [InlineData("missing-sid")]
    [InlineData("invalid-sid")]
    [InlineData("unknown-sid")]
    [InlineData("missing-exp")]
    [InlineData("unsigned")]
    [InlineData("algorithm")]
    [InlineData("type")]
    [InlineData("missing-user")]
    [InlineData("malformed")]
    [InlineData("absent")]
    public async Task InvalidOrMissingBearerTokensAreRejected(string failure)
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password });
        var session = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(session);
        var sessionId = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken).Claims.Single(x => x.Type == "sid").Value;
        if (failure != "absent")
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                failure == "malformed" ? "not-a-token" : CreateToken(user.Id, sessionId, failure));
        using var response = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, x => x.Scheme == "Bearer" && string.IsNullOrEmpty(x.Parameter));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("IDX", body);
        Assert.DoesNotContain("Exception", body);
        Assert.True(JsonDocument.Parse(body).RootElement.TryGetProperty("traceId", out _));
    }

    [PostgresFact]
    public async Task OlderPasswordHashIsUpgradedAfterSuccessfulLogin()
    {
        var (user, password) = await CreateUserAsync(oldHash: true);
        var previousHash = user.PasswordHash;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        var updated = await database.Users.SingleAsync(x => x.Id == user.Id);
        Assert.NotNull(updated.PasswordHash);
        Assert.NotEqual(previousHash, updated.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, CreateHasher().VerifyHashedPassword(updated, updated.PasswordHash, password));
    }

    [PostgresFact]
    public async Task AmbiguousEmailVariantsFailClosed()
    {
        var (user, password) = await CreateUserAsync();
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        database.Users.Add(new User { Email = user.Email.ToUpperInvariant(), DisplayName = "Ambiguous" });
        await database.SaveChangesAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task LoginValidatesBodyAndAppliesRateLimit()
    {
        await using var factory = CreateFactory(permitLimit: 2);
        using var client = factory.CreateClient();
        using var invalid = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "invalid", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var failed = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "not-a-real-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        using var rejected = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "not-a-real-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

    private WebApplicationFactory<be.Program> CreateFactory(int permitLimit = 1000) =>
        new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Helpdesk"] = fixture.ConnectionString,
                ["AuthRateLimit:PermitLimit"] = permitLimit.ToString(CultureInfo.InvariantCulture)
            })));

    private async Task<(User User, string Password)> CreateUserAsync(bool active = true, bool passwordless = false, bool corruptHash = false, bool oldHash = false)
    {
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        await new IdentityInitializer(database, CreateHasher()).SeedAsync();
        var password = Guid.NewGuid().ToString("N");
        var user = new User { Email = $"{Guid.NewGuid():N}@example.test", DisplayName = "Auth test user", IsActive = active };
        user.PasswordHash = passwordless ? null : corruptHash ? "invalid-hash" : CreateHasher(oldHash ? 10_000 : 210_000).HashPassword(user, password);
        database.Users.Add(user);
        var roles = await database.Roles.Where(x => x.Name == RoleNames.Requester || x.Name == RoleNames.SupportAgent).ToListAsync();
        foreach (var role in roles) database.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await database.SaveChangesAsync();
        return (user, password);
    }

    private static PasswordHasher<User> CreateHasher(int iterations = 210_000) =>
        new(Options.Create(new PasswordHasherOptions { IterationCount = iterations }));

    private static string CreateToken(Guid userId, string sessionId, string failure)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>();
        if (failure != "missing-sub") claims.Add(new("sub", failure == "invalid-sub" ? "invalid" : (failure == "missing-user" ? Guid.NewGuid() : userId).ToString()));
        if (failure != "missing-jti") claims.Add(new("jti", Guid.NewGuid().ToString("N")));
        if (failure != "missing-sid") claims.Add(new("sid", failure == "invalid-sid" ? "invalid" : failure == "unknown-sid" ? Guid.NewGuid().ToString() : sessionId));
        if (failure != "missing-iat") claims.Add(new("iat", EpochTime.GetIntDate(now).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        var key = failure == "signature" ? new string('x', 64) : AuthTestSettings.SigningKey;
        var credentials = failure == "unsigned" ? null : new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), failure == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            failure == "issuer" ? "WrongIssuer" : AuthTestSettings.Issuer,
            failure == "audience" ? "WrongAudience" : AuthTestSettings.Audience, claims,
            failure == "future" ? now.AddMinutes(10) : now.AddMinutes(-20),
            failure == "missing-exp" ? null : failure == "expired" ? now.AddMinutes(-10) : now.AddMinutes(20), credentials);
        token.Header["typ"] = failure == "type" ? "JWT" : "at+jwt";
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
