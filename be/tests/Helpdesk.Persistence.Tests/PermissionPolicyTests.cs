using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Api.Authorization;
using be.Application.Common.Authorization;
using be.Domain.Constants;
using be.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class PermissionPolicyTests
{
    [Fact]
    public async Task EveryKnownPermissionHasAnAuthenticatedBearerPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHelpdeskAuthorization();
        using var provider = services.BuildServiceProvider();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        Assert.Equal(13, PermissionCodes.All.Count);
        foreach (var permission in PermissionCodes.All)
        {
            var policy = await policies.GetPolicyAsync(permission);
            Assert.NotNull(policy);
            Assert.Equal(new[] { "Bearer" }, policy.AuthenticationSchemes);
            Assert.Contains(policy.Requirements, x => x is DenyAnonymousAuthorizationRequirement);
            Assert.Equal(permission, Assert.Single(policy.Requirements.OfType<PermissionRequirement>()).Permission);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("USER.MANAGE")]
    [InlineData("user.unknown")]
    [InlineData(RoleNames.SystemAdmin)]
    public void PermissionAttributeRejectsUnregisteredNames(string permission) =>
        Assert.Throws<ArgumentException>(() => new RequirePermissionAttribute(permission));

    [Theory]
    [InlineData("anonymous")]
    [InlineData("missing-sub")]
    [InlineData("invalid-sub")]
    [InlineData("empty-sub")]
    [InlineData("missing-sid")]
    [InlineData("invalid-sid")]
    [InlineData("empty-sid")]
    public async Task InvalidIdentityNeverReachesThePermissionQuery(string failure)
    {
        var claims = new List<Claim>();
        if (failure != "missing-sub") claims.Add(new("sub", failure == "invalid-sub" ? "invalid" :
            (failure == "empty-sub" ? Guid.Empty : Guid.NewGuid()).ToString()));
        if (failure != "missing-sid") claims.Add(new("sid", failure == "invalid-sid" ? "invalid" :
            (failure == "empty-sid" ? Guid.Empty : Guid.NewGuid()).ToString()));
        var identity = new ClaimsIdentity(claims, failure == "anonymous" ? null : "Bearer");
        var requirement = new PermissionRequirement(PermissionCodes.UserManage);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(identity), null);
        var checker = new RecordingPermissionChecker();
        await new PermissionAuthorizationHandler(checker, new HttpContextAccessor()).HandleAsync(context);
        Assert.False(context.HasSucceeded);
        Assert.False(checker.Called);
    }

    [Fact]
    public async Task HandlerPassesAuthenticatedIdsAndRequestCancellationToTheQuery()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var http = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var requirement = new PermissionRequirement(PermissionCodes.UserManage);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()), new Claim("sid", sessionId.ToString())
        }, "Bearer"));
        var context = new AuthorizationHandlerContext([requirement], principal, http);
        var checker = new RecordingPermissionChecker();
        await new PermissionAuthorizationHandler(checker, new HttpContextAccessor { HttpContext = http }).HandleAsync(context);
        Assert.True(context.HasSucceeded);
        Assert.Equal((userId, sessionId, PermissionCodes.UserManage, cancellation.Token), checker.Arguments);
    }

    [Fact]
    public async Task AnonymousPermissionRequestIs401AndLivenessRemainsPublic()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var denied = await client.GetAsync("/__tests/permissions/manage-users");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Contains(denied.Headers.WwwAuthenticate, x => x.Scheme == "Bearer");
        Assert.True((await denied.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("traceId", out _));
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task SwaggerDescribesPermissionPoliciesAsBearerWith401And403()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        foreach (var path in new[] { "/__tests/permissions/manage-users", "/__tests/team-permissions/assign" })
        {
            var operation = swagger.GetProperty("paths").GetProperty(path).GetProperty("get");
            Assert.True(operation.GetProperty("security")[0].TryGetProperty("Bearer", out _));
            var responses = operation.GetProperty("responses");
            Assert.True(responses.GetProperty("401").GetProperty("content").TryGetProperty("application/problem+json", out _));
            Assert.True(responses.GetProperty("403").GetProperty("content").TryGetProperty("application/problem+json", out _));
        }
        Assert.False(swagger.GetProperty("paths").GetProperty("/api/v1/auth/login").GetProperty("post").TryGetProperty("security", out _));
    }

    private sealed class RecordingPermissionChecker : IPermissionChecker
    {
        public bool Called { get; private set; }
        public (Guid UserId, Guid SessionId, string Permission, CancellationToken Cancellation) Arguments { get; private set; }
        public Task<bool> HasPermissionAsync(Guid userId, Guid sessionId, string permission, CancellationToken cancellationToken)
        {
            Called = true;
            Arguments = (userId, sessionId, permission, cancellationToken);
            return Task.FromResult(true);
        }
    }
}
