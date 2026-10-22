namespace be.Contracts.Responses;

public sealed record LoginResponse(
    string AccessToken, string TokenType, DateTime ExpiresAt, UserResponse User,
    string RefreshToken, DateTime RefreshTokenExpiresAt);
