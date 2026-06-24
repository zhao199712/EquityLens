namespace EquityLens.Api.Services.Chat;

public sealed record JinaSearchResult(
    string Title,
    string Url,
    string Content);

public sealed record JinaSearchResponse(
    string Query,
    IReadOnlyList<JinaSearchResult> Results);

public sealed record JinaRerankItem(
    int Index,
    double RelevanceScore,
    string Text);

public sealed record JinaRerankResponse(
    string Model,
    IReadOnlyList<JinaRerankItem> Results);

public interface IJinaSearchService
{
    Task<JinaSearchResponse> SearchAsync(
        string query,
        int numResults = 5,
        CancellationToken cancellationToken = default);

    Task<JinaRerankResponse> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken = default);
}
