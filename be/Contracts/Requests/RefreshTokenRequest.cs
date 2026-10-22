using System.ComponentModel.DataAnnotations;

namespace be.Contracts.Requests;

public sealed class RefreshTokenRequest
{
    [Required]
    [StringLength(86, MinimumLength = 86)]
    [RegularExpression("^[A-Za-z0-9_-]{86}$")]
    public string RefreshToken { get; init; } = "";
}
