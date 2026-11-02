namespace EquityLens.Api.Services.Ai;

public class DeepSeekOptions
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string ApiKey { get; set; } = "";
    /// <summary>null 沿用供應商預設；false 明確關閉 thinking，避免短 JSON 輸出預算被推理耗盡。</summary>
    public bool? ThinkingEnabled { get; set; }
    public string Model { get; set; } = "deepseek-flash";
}
