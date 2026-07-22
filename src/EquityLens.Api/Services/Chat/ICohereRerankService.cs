namespace EquityLens.Api.Services.Chat;

public sealed record CohereRerankItem(int Index, double RelevanceScore);

public sealed record CohereRerankResponse(
    string Model,
    IReadOnlyList<CohereRerankItem> Results,
    string Status,
    string? FallbackReason,
    long DurationMs,
    int CandidateCount,
    int PayloadBytes,
    int? HttpStatusCode);

public interface ICohereRerankService
{
    Task<CohereRerankResponse> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken = default);
}
