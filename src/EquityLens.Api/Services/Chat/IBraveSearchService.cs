namespace EquityLens.Api.Services.Chat;

public sealed record BraveSearchResult(
    string Title,
    string Url,
    string Description,
    string? PageAge);

public sealed record BraveSearchResponse(
    string Query,
    IReadOnlyList<BraveSearchResult> Results);

public interface IBraveSearchService
{
    Task<BraveSearchResponse> SearchAsync(
        string query,
        int count = 10,
        string? freshness = null,
        CancellationToken cancellationToken = default);
}
