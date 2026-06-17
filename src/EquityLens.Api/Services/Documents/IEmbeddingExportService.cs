namespace EquityLens.Api.Services.Documents;

public sealed record EmbeddingExportResult(int Exported, string OutputPath);

public interface IEmbeddingExportService
{
    Task<EmbeddingExportResult> ExportAsync(string outputPath, CancellationToken cancellationToken = default);
}
