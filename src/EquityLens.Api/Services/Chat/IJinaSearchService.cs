namespace EquityLens.Api.Services.Chat;

public sealed record JinaSearchResult(
    string Title,
    string Url,
    string Content);

public sealed record JinaSearchResponse(
    string Query,
    IReadOnlyList<JinaSearchResult> Results);

public interface IJinaSearchService
{
    Task<JinaSearchResponse> SearchAsync(
        string query,
        int numResults = 5,
        CancellationToken cancellationToken = default);
}
