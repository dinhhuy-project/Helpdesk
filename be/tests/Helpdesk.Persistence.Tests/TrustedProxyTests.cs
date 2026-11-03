using System.Net;
using System.Net.Http.Json;
using be.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class TrustedProxyTests
{
    [Theory]
    [InlineData("172.29.240.2", true, "https", "203.0.113.17")]
    [InlineData("172.29.240.9", true, "http", "172.29.240.9")]
    [InlineData("172.29.240.2", false, "http", "172.29.240.2")]
    public async Task OnlyAnExplicitlyTrustedProxyCanChangeClientIpAndScheme(string peer, bool enabled, string scheme, string ip)
    {
        await using var factory = new ApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Enabled"] = enabled.ToString(), ["ReverseProxy:KnownProxies:0"] = "172.29.240.2"
            }));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new RemotePeer(peer)));
        });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__tests/proxy");
        request.Headers.Add("X-Forwarded-Proto", "https"); request.Headers.Add("X-Forwarded-For", "203.0.113.17");
        request.Headers.Add("X-Forwarded-Host", "attacker.example");
        var result = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<ProxyResponse>();
        Assert.Equal(scheme, result!.Scheme); Assert.Equal(ip, result.Ip); Assert.Equal("localhost", result.Host);
    }

    [Fact]
    public async Task TrustedHttpsForwardingRunsBeforeProductionHttpsRedirection()
    {
        await using var factory = new ApiFactory("Production").WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Enabled"] = "true", ["ReverseProxy:KnownProxies:0"] = "172.29.240.2"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new RemotePeer("172.29.240.2"));
                services.AddHttpsRedirection(options => options.HttpsPort = 443);
            });
        });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var plain = await client.GetAsync("/health/live"); Assert.Equal(HttpStatusCode.TemporaryRedirect, plain.StatusCode);
        using var forwarded = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        forwarded.Headers.Add("X-Forwarded-Proto", "https"); forwarded.Headers.Add("X-Forwarded-For", "203.0.113.17");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(forwarded)).StatusCode);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("172.29.240.0/24")]
    [InlineData("proxy.example")]
    public void TrustRequiresExplicitAddresses(string address) => Assert.False(TrustedProxyOptions.Valid(new() { Enabled = true, KnownProxies = [address] }));

    private sealed class RemotePeer(string ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) => { context.Connection.RemoteIpAddress = IPAddress.Parse(ip); return continuation(context); });
            next(app);
        };
    }
}

public sealed record ProxyResponse(string Scheme, string Ip, string Host);

[ApiController]
public class ProxyTestController : ControllerBase
{
    [HttpGet("/__tests/proxy")]
    public ProxyResponse Inspect() => new(Request.Scheme, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", Request.Host.Host);
}
