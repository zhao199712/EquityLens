namespace EquityLens.Api.Contracts.Files;

/// <summary>
/// 檔案上傳回應資料。
/// </summary>
/// <param name="Id">檔案記錄的唯一識別碼。</param>
/// <param name="OriginalFileName">原始檔案名稱。</param>
/// <param name="ContentType">內容類型。</param>
/// <param name="FileSizeBytes">檔案大小（位元組）。</param>
/// <param name="ObjectKey">物件儲存中的金鑰。</param>
/// <param name="UploadStatus">上傳狀態，例如 "Uploaded" 或 "Failed"。</param>
/// <param name="CreatedAtUtc">建立時間（UTC）。</param>
public sealed record UploadedFileResponse(
    Guid Id,
    string OriginalFileName,
    string? ContentType,
    long FileSizeBytes,
    string ObjectKey,
    string UploadStatus,
    DateTime CreatedAtUtc);
