using UglyToad.PdfPig;

namespace EquityLens.Api.Services.DocumentParsing;

/// <summary>
/// PdfPig PDF 解析實現，逐頁提取文字內容。
/// </summary>
public sealed class PigPdfParser : IPdfParser
{
    /// <inheritdoc />
    public IReadOnlyList<ParsedPage> Parse(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var results = new List<ParsedPage>();

        for (var i = 1; i <= document.NumberOfPages; i++)
        {
            var page = document.GetPage(i);
            var content = page.Text ?? string.Empty;

            // 粗估 token 數量：中文約 1.5 token/字，英文約 0.75 token/word
            // 這裡用字元數 / 4 作為粗估
            var tokenCount = Math.Max(1, content.Length / 4);

            results.Add(new ParsedPage(i, content, tokenCount));
        }

        return results;
    }
}
