namespace EquityLens.Api.Data.Entities;

public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Portfolio> Portfolios { get; set; } = [];
    public ICollection<UploadedFile> UploadedFiles { get; set; } = [];
    public ICollection<JobRun> JobRuns { get; set; } = [];
}
