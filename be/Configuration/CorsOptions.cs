namespace be.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = [];

    public static bool IsValid(CorsOptions options) => options.AllowedOrigins is not null &&
        options.AllowedOrigins.All(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) &&
            origin == uri.GetLeftPart(UriPartial.Authority));
}
