using System.ComponentModel.DataAnnotations;

namespace be.Configuration;

public sealed class AuthRateLimitOptions
{
    public const string SectionName = "AuthRateLimit";
    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 5;
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}
