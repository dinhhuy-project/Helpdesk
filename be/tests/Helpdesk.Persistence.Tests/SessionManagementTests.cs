using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using be.Contracts.Responses;
using be.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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
public class SessionManagementTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task LoginStoresOnlyHashAndRotationReplacesThePairWithoutExtendingLifetime()
    {
        var (user, password) = await CreateUserAsync();
        var clock = new SessionTestClock();
        await using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        Assert.Matches("^[A-Za-z0-9_-]{86}$", login.RefreshToken);
        Assert.False(client.DefaultRequestHeaders.Contains("Cookie"));
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        var initial = await database.RefreshTokens.AsNoTracking().SingleAsync(x => x.UserId == user.Id);
        Assert.Equal(Hash(login.RefreshToken), initial.TokenHash);
        Assert.DoesNotContain(login.RefreshToken, initial.TokenHash);
        Assert.Null(initial.RevokedAt);
        Assert.Equal(initial.Id.ToString(), SessionId(login.AccessToken));
        Assert.InRange(login.RefreshTokenExpiresAt, clock.GetUtcNow().UtcDateTime.AddDays(7).AddSeconds(-1),
            clock.GetUtcNow().UtcDateTime.AddDays(7).AddSeconds(1));

        clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { login.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var rotated = await ReadTokensAsync(response);
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(login.AccessToken, rotated.AccessToken);
        Assert.Equal(user.Id, rotated.User.Id);
        Assert.Equal("Bearer", rotated.TokenType);
        Assert.InRange(rotated.RefreshTokenExpiresAt, login.RefreshTokenExpiresAt.AddMilliseconds(-1), login.RefreshTokenExpiresAt);
        var stored = await database.RefreshTokens.AsNoTracking().Where(x => x.UserId == user.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        var previous = stored.Single(x => x.Id == initial.Id);
        var next = stored.Single(x => x.Id != initial.Id);
        Assert.NotNull(previous.RevokedAt);
        Assert.Equal(next.Id, previous.ReplacedByTokenId);
        Assert.Equal(Hash(rotated.RefreshToken), next.TokenHash);
        Assert.Null(next.RevokedAt);
        Assert.Equal(next.Id.ToString(), SessionId(rotated.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, login.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(client, rotated.AccessToken));
    }

    [PostgresFact]
    public async Task ReusingAnAncestorRevokesItsEntireChainButPreservesAnotherLogin()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var first = await LoginAsync(client, user, password);
        var otherDevice = await LoginAsync(client, user, password);
        var second = await RefreshAsync(client, first.RefreshToken);
        var third = await RefreshAsync(client, second.RefreshToken);
        using var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { first.RefreshToken });
        await AssertSessionFailureAsync(replay);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, third.AccessToken));
        using var latest = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { third.RefreshToken });
        await AssertSessionFailureAsync(latest);
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(client, otherDevice.AccessToken));
        var surviving = await RefreshAsync(client, otherDevice.RefreshToken);
        Assert.Equal(user.Id, surviving.User.Id);
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        var chainIds = new[] { first, second, third }.Select(x => Guid.Parse(SessionId(x.AccessToken))).ToArray();
        Assert.Equal(3, await database.RefreshTokens.CountAsync(x => chainIds.Contains(x.Id) && x.RevokedAt != null));
    }

    [PostgresFact]
    public async Task LogoutWithAnOldTokenRevokesTheCurrentPairAndIsIdempotent()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        var rotated = await RefreshAsync(client, login.RefreshToken);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { login.RefreshToken });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.True(logout.Headers.CacheControl?.NoStore);
            Assert.Empty(await logout.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, rotated.AccessToken));
        using var failed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { rotated.RefreshToken });
        await AssertSessionFailureAsync(failed);
        using var unknown = await client.PostAsJsonAsync("/api/v1/auth/logout", new { RefreshToken = new string('x', 86) });
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
    }

    [PostgresFact]
    public async Task ExpiredAccessTokenCannotCallMeButCanRefreshAndLogout()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        var expired = RewriteAccessToken(login.AccessToken, user.Id, expired: true);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, expired));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        var refreshed = await RefreshAsync(client, login.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(client, refreshed.AccessToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        using var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshed.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, refreshed.AccessToken));
    }

    [PostgresFact]
    public async Task RefreshExpiresAtTheOriginalDeadlineEvenAfterRotation()
    {
        var (user, password) = await CreateUserAsync();
        var clock = new SessionTestClock();
        await using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        clock.Advance(TimeSpan.FromDays(6));
        var rotated = await RefreshAsync(client, login.RefreshToken);
        clock.Advance(TimeSpan.FromDays(1));
        using var expired = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { rotated.RefreshToken });
        await AssertSessionFailureAsync(expired);
        using var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [PostgresFact]
    public async Task ExpiredRefreshSessionAlsoRejectsAnOtherwiseValidAccessToken()
    {
        var (user, password) = await CreateUserAsync();
        var clock = new SessionTestClock();
        await using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(client, login.AccessToken));
        clock.Advance(TimeSpan.FromDays(7));
        // JWT wall-clock lifetime is still valid; the persisted session has reached its absolute deadline.
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, login.AccessToken));
    }

    [PostgresFact]
    public async Task InactiveUserCannotRefreshAndTheAttemptPermanentlyRevokesThatChain()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, false));
        using var inactive = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { login.RefreshToken });
        await AssertSessionFailureAsync(inactive);
        await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, true));
        using var retry = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { login.RefreshToken });
        await AssertSessionFailureAsync(retry);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, login.AccessToken));
    }

    [PostgresFact]
    public async Task SessionIdMustBelongToTheTokenSubject()
    {
        var (owner, password) = await CreateUserAsync();
        var (another, _) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, owner, password);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, RewriteAccessToken(login.AccessToken, another.Id)));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(client, login.AccessToken));
    }

    [PostgresFact]
    public async Task ConcurrentRefreshesHaveOneWinnerAndReplayRevokesItsResult()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/refresh", new { login.RefreshToken })));
        try
        {
            var winner = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Equal(3, responses.Count(x => x.StatusCode == HttpStatusCode.Unauthorized));
            var result = await ReadTokensAsync(winner);
            Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, result.AccessToken));
            using var retry = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { result.RefreshToken });
            await AssertSessionFailureAsync(retry);
            await using var database = ModelTests.CreateContext(fixture.ConnectionString);
            Assert.Equal(2, await database.RefreshTokens.CountAsync(x => x.UserId == user.Id));
            Assert.False(await database.RefreshTokens.AnyAsync(x => x.UserId == user.Id && x.RevokedAt == null));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task ConcurrentRefreshAndLogoutLeaveNoUsablePair()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        var refreshTask = client.PostAsJsonAsync("/api/v1/auth/refresh", new { login.RefreshToken });
        var logoutTask = client.PostAsJsonAsync("/api/v1/auth/logout", new { login.RefreshToken });
        await Task.WhenAll(refreshTask, logoutTask);
        using var refresh = await refreshTask;
        using var logout = await logoutTask;
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(refresh.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized });
        if (refresh.IsSuccessStatusCode)
        {
            var result = await ReadTokensAsync(refresh);
            Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, result.AccessToken));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, login.AccessToken));
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        Assert.False(await database.RefreshTokens.AnyAsync(x => x.UserId == user.Id && x.RevokedAt == null));
    }

    [PostgresFact]
    public async Task UnknownAndRevokedRefreshTokensUseTheSameSanitizedFailure()
    {
        var (user, password) = await CreateUserAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, user, password);
        using var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { login.RefreshToken });
        using var revoked = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { login.RefreshToken });
        using var unknown = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = new string('x', 86) });
        await AssertSessionFailureAsync(revoked);
        await AssertSessionFailureAsync(unknown);
        Assert.Equal((await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString(),
            (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.DoesNotContain(login.RefreshToken, await revoked.Content.ReadAsStringAsync());
    }

    [PostgresTheory]
    [InlineData("refresh", "")]
    [InlineData("refresh", "short")]
    [InlineData("logout", "")]
    [InlineData("logout", "short")]
    [InlineData("refresh", "invalid-characters")]
    public async Task RefreshAndLogoutValidateCredentials(string action, string value)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/auth/{action}",
            new { RefreshToken = value == "invalid-characters" ? new string('!', 86) : value });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(JsonDocument.Parse(body).RootElement.TryGetProperty("traceId", out _));
        if (value.Length > 0) Assert.DoesNotContain($"\"{value}\"", body);
    }

    [PostgresTheory]
    [InlineData("refresh")]
    [InlineData("logout")]
    public async Task SessionEndpointsShareTheAuthenticationRateLimit(string action)
    {
        await using var factory = CreateFactory(permitLimit: 1);
        using var client = factory.CreateClient();
        using var first = await client.PostAsJsonAsync($"/api/v1/auth/{action}", new { RefreshToken = new string('x', 86) });
        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        using var second = await client.PostAsJsonAsync($"/api/v1/auth/{action}", new { RefreshToken = new string('x', 86) });
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.NotNull(second.Headers.RetryAfter);
    }

    [PostgresTheory]
    [InlineData("refresh")]
    [InlineData("logout")]
    public async Task FormCredentialsAreNotAccepted(string action)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/v1/auth/{action}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["refreshToken"] = new string('x', 86) }));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    private WebApplicationFactory<be.Program> CreateFactory(SessionTestClock? clock = null, int permitLimit = 1000) =>
        new ApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Helpdesk"] = fixture.ConnectionString,
                ["AuthRateLimit:PermitLimit"] = permitLimit.ToString(CultureInfo.InvariantCulture),
                ["Jwt:RefreshTokenDays"] = "7"
            }));
            if (clock is not null) builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        });

    private async Task<(User User, string Password)> CreateUserAsync()
    {
        var password = Guid.NewGuid().ToString("N");
        var user = new User { Email = $"{Guid.NewGuid():N}@example.test", DisplayName = "Session test user" };
        user.PasswordHash = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }))
            .HashPassword(user, password);
        await using var database = ModelTests.CreateContext(fixture.ConnectionString);
        database.Users.Add(user);
        await database.SaveChangesAsync();
        return (user, password);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, User user, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { user.Email, password });
        return await ReadTokensAsync(response);
    }

    private static async Task<LoginResponse> RefreshAsync(HttpClient client, string refreshToken)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        return await ReadTokensAsync(response);
    }

    private static async Task<LoginResponse> ReadTokensAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        return result;
    }

    private static async Task<HttpStatusCode> MeStatusAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task AssertSessionFailureAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        Assert.True(json.TryGetProperty("traceId", out _));
        Assert.False(json.TryGetProperty("accessToken", out _));
        Assert.False(json.TryGetProperty("refreshToken", out _));
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("tokenHash", body, StringComparison.OrdinalIgnoreCase);
    }

    private static string SessionId(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Single(x => x.Type == "sid").Value;

    private static string RewriteAccessToken(string token, Guid subject, bool expired = false)
    {
        var original = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var claims = original.Claims.Where(x => x.Type is "sid" or "iat" or "jti").ToList();
        claims.Add(new("sub", subject.ToString()));
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(AuthTestSettings.Issuer, AuthTestSettings.Audience, claims,
            now.AddMinutes(-20), expired ? now.AddMinutes(-10) : now.AddMinutes(15),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTestSettings.SigningKey)), SecurityAlgorithms.HmacSha256));
        jwt.Header["typ"] = "at+jwt";
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class SessionTestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
