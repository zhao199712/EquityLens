using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.ObjectStorage;

/// <summary>
/// S3 相容物件儲存服務實現，使用 AWSSDK.S3 連線 Garage 等 S3 API。
/// </summary>
public sealed class S3ObjectStorageService : IObjectStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly ObjectStorageOptions _options;

    /// <summary>
    /// 初始化 S3 物件儲存服務。
    /// </summary>
    /// <param name="s3Client">AWS S3 客戶端。</param>
    /// <param name="options">物件儲存設定選項。</param>
    public S3ObjectStorageService(IAmazonS3 s3Client, IOptions<ObjectStorageOptions> options)
    {
        _s3Client = s3Client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<PutObjectResponse> UploadAsync(
        string objectKey,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType ?? "application/octet-stream",
            AutoCloseStream = false
        };

        return await _s3Client.PutObjectAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var response = await _s3Client.GetObjectAsync(_options.BucketName, objectKey, cancellationToken);
        return response.ResponseStream;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        await _s3Client.DeleteObjectAsync(_options.BucketName, objectKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> CreatePresignedDownloadUrlAsync(
        string objectKey,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        var expiry = expiresIn ?? TimeSpan.FromMinutes(_options.PresignedUrlExpiryMinutes);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET
        };

        return Task.FromResult(_s3Client.GetPreSignedURL(request));
    }
}
