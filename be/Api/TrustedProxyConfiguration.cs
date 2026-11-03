using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace be.Api;

public sealed class TrustedProxyOptions
{
    public bool Enabled { get; set; }
    public string[] KnownProxies { get; set; } = [];
    public static bool Valid(TrustedProxyOptions options) => !options.Enabled ||
        options.KnownProxies is { Length: > 0 and <= 8 } && options.KnownProxies.All(value =>
            IPAddress.TryParse(value, out var address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any));
}

public static class TrustedProxyConfiguration
{
    public static IServiceCollection AddTrustedProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TrustedProxyOptions>().Bind(configuration.GetSection("ReverseProxy"))
            .Validate(TrustedProxyOptions.Valid, "Enabled ReverseProxy requires explicit KnownProxies IP addresses (maximum eight).")
            .ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<TrustedProxyOptions>>((headers, settings) =>
        {
            headers.ForwardedHeaders = ForwardedHeaders.None;
            if (!settings.Value.Enabled) return;
            headers.KnownNetworks.Clear(); headers.KnownProxies.Clear();
            foreach (var proxy in settings.Value.KnownProxies) headers.KnownProxies.Add(IPAddress.Parse(proxy));
            headers.ForwardLimit = 1;
            headers.RequireHeaderSymmetry = true;
            headers.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Host is validated by AllowedHosts; never trust forwarded host from arbitrary clients.
        });
        return services;
    }
}
