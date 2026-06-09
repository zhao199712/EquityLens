using EquityLens.Api.Contracts.Files;

namespace EquityLens.Api.Services.UploadedFiles;

/// <summary>
/// 已上傳檔案服務介面，負責檔案上傳、查詢與刪除的業務邏輯。
/// </summary>
public interface IUploadedFileService
{
    /// <summary>
    /// 上傳檔案到物件儲存，並建立資料庫記錄。
    /// </summary>
    /// <param name="file">要上傳的表單檔案。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>已建立檔案記錄的回應資料。</returns>
    Task<UploadedFileResponse> UploadAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得當前使用者的所有已上傳檔案列表。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>檔案回應列表。</returns>
    Task<IReadOnlyList<UploadedFileResponse>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 依據檔案識別碼取得檔案記錄。
    /// </summary>
    /// <param name="id">檔案的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>檔案回應資料；若不存在則返回 null。</returns>
    Task<UploadedFileResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 產生指定檔案的限時下載 URL。
    /// </summary>
    /// <param name="id">檔案的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>Presigned URL 回應；若檔案不存在則返回 null。</returns>
    Task<PresignedUrlResponse?> CreateDownloadUrlAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 刪除指定檔案的物件儲存資料與資料庫記錄。
    /// </summary>
    /// <param name="id">檔案的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>是否成功刪除；若檔案不存在則返回 false。</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
