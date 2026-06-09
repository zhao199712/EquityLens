namespace EquityLens.Api.Contracts.Auth;

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    UserInfo User);
