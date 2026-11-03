using System.Security.Cryptography;
using System.Text;

namespace EquityLens.Api.Services.Auth;

/// <summary>只儲存隨機 refresh token 的 SHA-256 摘要，格式標記用於辨識舊明文資料。</summary>
public static class RefreshTokenDigest
{
    public static string Compute(string token) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
