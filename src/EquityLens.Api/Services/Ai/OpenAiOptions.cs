namespace EquityLens.Api.Services.Ai;

/// <summary>OpenAI Responses API 設定；金鑰由私有設定或 OPENAI_API_KEY 提供。</summary>
public sealed class OpenAiOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-6-luna";
    // 既有迴圈自行執行工具；低推理模式用於多步 JSON 決策，計入同一輸出預算。
    public string ReasoningEffort { get; set; } = "low";
}
