namespace EquityLens.Api.Services.Chat;

public sealed class JinaOptions
{
    public const string SectionName = "Jina";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.jina.ai/v1";
    public string RerankModel { get; set; } = "jina-reranker-v3";
}
