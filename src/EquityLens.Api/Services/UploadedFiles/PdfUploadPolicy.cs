using UglyToad.PdfPig;

namespace EquityLens.Api.Services.UploadedFiles;

/// <summary>上傳僅接受有大小限制且可解析的 PDF；不將此檢查視為惡意程式掃描。</summary>
public static class PdfUploadPolicy
{
    public const long MaxFileBytes = 20 * 1024 * 1024;
    public const long MaxRequestBytes = MaxFileBytes + 1024 * 1024;

    public static void Validate(IFormFile file)
    {
        if (file.Length <= 0)
            throw new BadHttpRequestException("請選擇要上傳的 PDF。", StatusCodes.Status400BadRequest);
        if (file.Length > MaxFileBytes)
            throw new BadHttpRequestException("PDF 不得超過 20 MiB。", StatusCodes.Status413PayloadTooLarge);
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(file.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)))
            throw new BadHttpRequestException("僅支援 PDF 檔案。", StatusCodes.Status415UnsupportedMediaType);

        try
        {
            using var stream = file.OpenReadStream();
            Span<byte> signature = stackalloc byte[5];
            stream.ReadExactly(signature);
            if (!signature.SequenceEqual("%PDF-"u8)) throw new InvalidDataException();
            stream.Position = 0;
            using var document = PdfDocument.Open(stream);
            if (document.NumberOfPages < 1) throw new InvalidDataException();
            _ = document.GetPage(1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
        {
            throw new BadHttpRequestException("檔案不是可讀取的 PDF，或已加密。", StatusCodes.Status415UnsupportedMediaType);
        }
    }
}
