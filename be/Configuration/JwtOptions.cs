using System.ComponentModel.DataAnnotations;
using System.Text;

namespace be.Configuration;

// Configuration contract for the next authentication step; does not issue or validate tokens yet.
public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";
    public bool RequireConfiguration { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? SigningKey { get; set; }
    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;
    [Range(1, 30)]
    public int RefreshTokenDays { get; set; } = 7;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Optional until authentication is implemented; a partially configured section must fail validation.
        if (!RequireConfiguration && Issuer is null && Audience is null && SigningKey is null) yield break;
        if (string.IsNullOrWhiteSpace(Issuer))
            yield return new ValidationResult("Jwt:Issuer is required when JWT is configured.");
        if (string.IsNullOrWhiteSpace(Audience))
            yield return new ValidationResult("Jwt:Audience is required when JWT is configured.");
        if (string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32)
            yield return new ValidationResult("Jwt:SigningKey must contain at least 32 bytes when JWT is configured.");
    }
}
