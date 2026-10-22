using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be;
using be.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public sealed class ApiFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
            ["AuthRateLimit:PermitLimit"] = "2",
            ["AuthRateLimit:WindowSeconds"] = "60"
        }));
        builder.ConfigureTestServices(services => services.AddControllers().AddApplicationPart(typeof(FoundationTestController).Assembly));
    }
}

public class ApiFoundationTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnexpectedExceptionsAreSanitizedAndHaveTraceId(string environment)
    {
        await using var factory = new ApiFactory(environment);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/__tests/error");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-marker", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.False(string.IsNullOrEmpty(JsonDocument.Parse(body).RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ValidationAndNotFoundUseProblemDetails()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var invalid = await client.PostAsJsonAsync("/__tests/validate", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var validation = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(validation.GetProperty("errors").TryGetProperty("Name", out _));
        Assert.True(validation.TryGetProperty("traceId", out _));
        using var missing = await client.GetAsync("/__missing");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ConcurrencyConflictReturns409WithoutDatabaseDetails()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/__tests/conflict");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("secret-marker", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CorsAllowsOnlyConfiguredOrigin()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        foreach (var allowed in new[] { true, false })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/__tests/validate");
            request.Headers.Add("Origin", allowed ? "http://localhost:5173" : "https://untrusted.example");
            request.Headers.Add("Access-Control-Request-Method", "POST");
            using var response = await client.SendAsync(request);
            Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        }
    }

    [Fact]
    public async Task AuthenticationPolicyReturns429AfterLimit()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var first = await client.GetAsync("/__tests/limited");
        using var second = await client.GetAsync("/__tests/limited");
        using var rejected = await client.GetAsync("/__tests/limited");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(rejected.Headers.RetryAfter);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("traceId", out _));
    }
}

// Discovered only by ApiFactory, never added to the production application's controllers.
[ApiController]
[Route("__tests")]
public class FoundationTestController : ControllerBase
{
    [HttpGet("error")]
    public IActionResult Error() => throw new InvalidOperationException("secret-marker");
    [HttpGet("conflict")]
    public IActionResult ConflictTest() => throw new DbUpdateConcurrencyException("secret-marker");
    [HttpPost("validate")]
    public IActionResult ValidateInput(ValidationInput input) => Ok();
    [HttpGet("limited")]
    [EnableRateLimiting(ApiPolicies.AuthenticationRateLimit)]
    public IActionResult Limited() => Ok();
}

public sealed class ValidationInput
{
    [Required]
    public string? Name { get; set; }
}
