namespace EquityLens.Api.Contracts.Auth;

public sealed record UserInfo(Guid Id, string Email, string DisplayName, string Role);
