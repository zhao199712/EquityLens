using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Documents;

public interface IDocumentSearchService
{
    Task<DocumentSearchResponse> SearchAsync(DocumentSearchRequest request, CancellationToken cancellationToken = default);
}
