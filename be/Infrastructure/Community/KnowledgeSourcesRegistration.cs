using System.Net;
using be.Application.Features.Community;
using be.Configuration;

namespace be.Infrastructure.Community;

public static class KnowledgeSourcesRegistration
{
    public static IServiceCollection AddKnowledgeSources(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KnowledgeSourcesOptions>().Bind(configuration.GetSection(KnowledgeSourcesOptions.Section))
            .Validate(Valid, "KnowledgeSources requires a valid user agent and, when enabled, a trusted BookStack URL, scoped token, book ID and source identifier.")
            .ValidateOnStart();
        services.AddHttpClient(KnowledgeSources.ClientName).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
        services.AddSingleton<IKnowledgeSources, KnowledgeSources>();
        services.AddScoped<KnowledgeSourceService>();
        return services;
    }
    public static bool Valid(KnowledgeSourcesOptions o)
    {
        using var probe = new HttpRequestMessage();
        if (string.IsNullOrWhiteSpace(o.UserAgent) || o.UserAgent.Length > 300 || o.UserAgent.Any(c => c is '\r' or '\n') || !probe.Headers.UserAgent.TryParseAdd(o.UserAgent)) return false;
        var b = o.BookStack;
        if (!b.Enabled) return true;
        if (!Url(b.BaseUrl, b.AllowHttp) || b.BookId <= 0 || string.IsNullOrEmpty(b.SourceId) || b.SourceId.Length > 60 ||
            b.SourceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') || !Token(b.TokenId) || !Token(b.TokenSecret) ||
            string.IsNullOrWhiteSpace(b.Attribution) || b.Attribution.Length > 300 || b.Attribution.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(b.License) || b.License.Length > 150 || b.License.Any(char.IsControl)) return false;
        // Publishing a private wiki address is unnecessary. Source links are optional and must be HTTPS.
        return string.IsNullOrEmpty(b.PublicBaseUrl) || Url(b.PublicBaseUrl, false);
    }
    private static bool Token(string value) => value.Length is >= 8 and <= 200 && value.All(char.IsAsciiLetterOrDigit);
    private static bool Url(string value, bool allowHttp) => Uri.TryCreate(value, UriKind.Absolute, out var u) &&
        (u.Scheme == "https" || (allowHttp && u.Scheme == "http")) && string.IsNullOrEmpty(u.UserInfo) &&
        string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment) && u.AbsolutePath.EndsWith('/') && value.Length <= 500;
}
