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
            AutoCloseStream = false,
            UseChunkEncoding = false
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

        if (!Uri.TryCreate(_options.PublicServiceUrl, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.IsLoopback
            || endpoint.HostNameType != UriHostNameType.Dns || !endpoint.Host.Contains('.'))
            throw new InvalidOperationException("ObjectStorage:PublicServiceUrl must be an external HTTPS origin.");
        if (!string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment) || endpoint.AbsolutePath != "/")
            throw new InvalidOperationException("ObjectStorage:PublicServiceUrl must be an external HTTPS origin.");
        if (expiry <= TimeSpan.Zero || expiry > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(expiresIn), "Download URLs must expire within one hour.");

        // 簽署外部原點；不可事後替換內部 URL 的 host，否則簽章無效。
        using var signer = new AmazonS3Client(_options.AccessKeyId, _options.SecretAccessKey, new AmazonS3Config
        {
            ServiceURL = endpoint.GetLeftPart(UriPartial.Authority),
            ForcePathStyle = true,
            AuthenticationRegion = _options.Region
        });
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET,
            Protocol = Protocol.HTTPS,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = "attachment",
                ContentType = "application/octet-stream"
            }
        };

        return Task.FromResult(signer.GetPreSignedURL(request));
    }
}
