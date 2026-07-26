using Google.Apis.Auth;

namespace EquityLens.Api.Services.Auth;

/// <summary>
/// Google ID Token 驗證結果中的使用者資訊。
/// </summary>
public sealed record GoogleUserPayload(
    string Subject,
    string Email,
    bool EmailVerified,
    string? Name);

/// <summary>
/// Google ID Token 驗證器，負責驗證前端取得的 Google ID Token。
/// </summary>
public interface IGoogleTokenValidator
{
    /// <summary>
    /// 驗證 Google ID Token，成功時回傳使用者資訊，失敗時回傳 null。
    /// </summary>
    Task<GoogleUserPayload?> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// 使用 Google.Apis.Auth 驗證 Google ID Token 簽章與 Audience。
/// </summary>
public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly string _clientId;

    public GoogleTokenValidator(IConfiguration configuration)
    {
        _clientId = configuration["Google:ClientId"]
            ?? throw new InvalidOperationException("Google:ClientId is not configured");
    }

    public async Task<GoogleUserPayload?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _clientId }
                });

            return new GoogleUserPayload(
                Subject: payload.Subject,
                Email: payload.Email,
                EmailVerified: payload.EmailVerified,
                Name: payload.Name);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
        catch (FormatException)
        {
            // 非 Base64Url 格式的 token 在解析階段就會失敗，同樣視為驗證失敗。
            return null;
        }
    }
}
