using Amazon.S3.Model;

namespace EquityLens.Api.Services.ObjectStorage;

/// <summary>
/// S3 相容物件儲存服務介面，提供上傳、下載、刪除與 presigned URL 功能。
/// </summary>
public interface IObjectStorageService
{
    /// <summary>
    /// 將資料流上傳到物件儲存。
    /// </summary>
    /// <param name="objectKey">物件金鑰（路徑）。</param>
    /// <param name="content">要上傳的資料流。</param>
    /// <param name="contentType">內容類型，例如 "application/pdf"。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>上傳結果，包含 ETag 與物件金鑰。</returns>
    Task<PutObjectResponse> UploadAsync(
        string objectKey,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 從物件儲存下載指定物件的資料流。
    /// </summary>
    /// <param name="objectKey">物件金鑰（路徑）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>物件資料流；呼叫端需負責釋放。</returns>
    Task<Stream> DownloadAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 從物件儲存刪除指定物件。
    /// </summary>
    /// <param name="objectKey">物件金鑰（路徑）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 產生指定物件的限時下載 URL。
    /// </summary>
    /// <param name="objectKey">物件金鑰（路徑）。</param>
    /// <param name="expiresIn">URL 有效時間；若為 null 則使用設定中的預設值。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>限時下載 URL 字串。</returns>
    Task<string> CreatePresignedDownloadUrlAsync(
        string objectKey,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default);
}
