namespace EquityLens.Api.Services.DocumentParsing;

/// <summary>
/// PDF 解析介面，將 PDF 檔案轉為逐頁文字內容。
/// </summary>
public interface IPdfParser
{
    /// <summary>
    /// 解析 PDF 並逐頁回傳文字內容。
    /// </summary>
    /// <param name="pdfBytes">PDF 檔案位元組陣列。</param>
    /// <returns>逐頁解析結果列表。</returns>
    IReadOnlyList<ParsedPage> Parse(byte[] pdfBytes);
}

/// <summary>
/// 單頁解析結果。
/// </summary>
/// <param name="PageNumber">頁碼（1-indexed）。</param>
/// <param name="Content">頁面文字內容。</param>
/// <param name="TokenCount">粗估 token 數量（字元數 / 4）。</param>
public sealed record ParsedPage(int PageNumber, string Content, int TokenCount);
