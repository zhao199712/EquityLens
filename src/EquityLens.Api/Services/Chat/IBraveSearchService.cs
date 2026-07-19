namespace EquityLens.Api.Services.Chat;

public sealed record BraveSearchResult(
    string Title,
    string Url,
    string Description,
    string? PageAge);

public sealed record BraveSearchResponse(
    string Query,
    IReadOnlyList<BraveSearchResult> Results);

public static class WebProviderErrorCodes
{
    public const string Configuration = "Configuration";
    public const string Unauthorized = "Unauthorized";
    public const string RateLimited = "RateLimited";
    public const string ProviderError = "ProviderError";
    public const string NetworkError = "NetworkError";
    public const string Timeout = "Timeout";
    public const string InvalidResponse = "InvalidResponse";

    public static bool IsTransient(string errorCode, int? httpStatusCode = null) => errorCode switch
    {
        NetworkError or Timeout or RateLimited or InvalidResponse => true,
        ProviderError => httpStatusCode is 408 or >= 500,
        _ => false
    };
}

public sealed class WebProviderException(string provider, string errorCode, int? httpStatusCode = null, Exception? innerException = null)
    : Exception($"{provider} web search failed ({errorCode}{(httpStatusCode is null ? string.Empty : $", HTTP {httpStatusCode}")}).", innerException)
{
    public string Provider { get; } = provider;
    public string ErrorCode { get; } = errorCode;
    public int? HttpStatusCode { get; } = httpStatusCode;
    public bool IsTransient => WebProviderErrorCodes.IsTransient(ErrorCode, HttpStatusCode);
}

public interface IBraveSearchService
{
    Task<BraveSearchResponse> SearchAsync(
        string query,
        int count = 10,
        string? freshness = null,
        CancellationToken cancellationToken = default);
}
