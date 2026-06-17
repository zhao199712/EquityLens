namespace EquityLens.Api.Services.FinancialData;

public sealed record TwseReportDownloadResult(int Attempted, int Succeeded, int Skipped, int Failed, string ManifestPath);

public interface ITwseReportDownloadService
{
    Task<TwseReportDownloadResult> DownloadAnnualReportsAsync(
        IReadOnlyList<int> rocYears,
        string outputDir,
        CancellationToken cancellationToken = default);
}
