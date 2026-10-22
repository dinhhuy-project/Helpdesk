using System.ComponentModel.DataAnnotations;

namespace be.Infrastructure.Identity;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Email) || Email.Trim().Length > 320 ||
            !new EmailAddressAttribute().IsValid(Email.Trim()))
            throw new IdentityInitializationException("BootstrapAdmin:Email must be a valid email with at most 320 characters.");
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Trim().Length > 200)
            throw new IdentityInitializationException("BootstrapAdmin:DisplayName must contain 1 to 200 characters.");
        if (string.IsNullOrWhiteSpace(Password) || Password.Length is < 12 or > 128)
            throw new IdentityInitializationException("BootstrapAdmin:Password must contain 12 to 128 characters.");
    }
}
