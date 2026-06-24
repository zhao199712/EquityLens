namespace EquityLens.Api.Services.Chat;

public sealed record CohereRerankItem(int Index, double RelevanceScore);

public sealed record CohereRerankResponse(string Model, IReadOnlyList<CohereRerankItem> Results);

public interface ICohereRerankService
{
    Task<CohereRerankResponse> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken = default);
}
