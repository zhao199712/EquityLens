namespace EquityLens.Api.Services.Documents;

public sealed record PdfPageText(int PageNumber, string Text);

public interface IPdfTextExtractionService
{
    Task<IReadOnlyList<PdfPageText>> ExtractPagesAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}
