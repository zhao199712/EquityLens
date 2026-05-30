namespace EquityLens.Api.Data.Entities;

public class CriticNote
{
    public Guid Id { get; set; }
    public Guid AiMemoId { get; set; }
    public string ReviewerType { get; set; } = string.Empty; // AI, Human
    public string? ReviewerName { get; set; }
    public string Severity { get; set; } = "Info"; // Info, Warning, Error
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public AiMemo AiMemo { get; set; } = null!;
}
