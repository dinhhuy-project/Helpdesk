namespace be.Infrastructure.Identity;

// Messages in this type are fixed operator guidance, never interpolated credentials or database details.
public sealed class IdentityInitializationException(string message) : InvalidOperationException(message);
