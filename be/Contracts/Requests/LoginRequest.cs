using System.ComponentModel.DataAnnotations;

namespace be.Contracts.Requests;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = "";
    [Required, StringLength(128)]
    public string Password { get; init; } = "";
}
