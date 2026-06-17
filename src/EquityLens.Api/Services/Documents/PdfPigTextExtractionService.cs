using UglyToad.PdfPig;

namespace EquityLens.Api.Services.Documents;

public sealed class PdfPigTextExtractionService : IPdfTextExtractionService
{
    public async Task<IReadOnlyList<PdfPageText>> ExtractPagesAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        await using var buffer = new MemoryStream();
        await pdfStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var document = PdfDocument.Open(buffer);
        var pages = new List<PdfPageText>();

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pages.Add(new PdfPageText(page.Number, NormalizeText(page.Text)));
        }

        return pages;
    }

    private static string NormalizeText(string text)
    {
        return string.Join('\n', text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));
    }
}
