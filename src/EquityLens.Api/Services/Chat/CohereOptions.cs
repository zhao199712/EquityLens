namespace EquityLens.Api.Services.Chat;

public sealed class CohereOptions
{
    public const string SectionName = "Cohere";

    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.cohere.com/v2";
    public string RerankModel { get; set; } = "rerank-v4.0-fast";
}
