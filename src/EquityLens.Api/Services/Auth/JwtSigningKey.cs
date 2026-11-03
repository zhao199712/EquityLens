using System.Text;

namespace EquityLens.Api.Services.Auth;

/// <summary>在啟動與簽章前檢查 HS256 金鑰，不將金鑰內容寫入錯誤。</summary>
public static class JwtSigningKey
{
    public static byte[] GetBytes(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes. Use a cryptographically random key.");
        return Encoding.UTF8.GetBytes(key);
    }
}
