namespace EquityLens.Api.Data.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    // Existing column name retained; value is sha256:<hex>, never the bearer credential.
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
    public bool IsRevoked { get; set; } = false;
    public bool IsUsed { get; set; } = false;

    // Navigation
    public AppUser User { get; set; } = null!;
}
