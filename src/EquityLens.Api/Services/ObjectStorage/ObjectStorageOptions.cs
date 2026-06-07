namespace EquityLens.Api.Services.ObjectStorage;

/// <summary>
/// S3 相容物件儲存（Garage）連線與行為設定選項。
/// </summary>
public sealed class ObjectStorageOptions
{
    /// <summary>
    /// S3 API 服務端點 URL，例如 "http://localhost:9000"。
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>
    /// S3 區域代碼，Garage 通常為 "garage"。
    /// </summary>
    public string Region { get; set; } = "garage";

    /// <summary>
    /// 預設儲存貯體名稱。
    /// </summary>
    public string BucketName { get; set; } = "equitylens";

    /// <summary>
    /// S3 存取金鑰識別碼。
    /// </summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>
    /// S3 私密存取金鑰。
    /// </summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// 是否強制使用路徑樣式存取（Garage 需要設為 true）。
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>
    /// Presigned URL 預設過期時間（分鐘），預設為 15 分鐘。
    /// </summary>
    public int PresignedUrlExpiryMinutes { get; set; } = 15;
}
