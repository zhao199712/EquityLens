using Amazon.S3;
using EquityLens.Api.Services.ObjectStorage;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;

namespace EquityLens.Api.Tests.Services.ObjectStorage;

public sealed class DownloadUrlSecurityTests
{
    [Fact]
    public async Task DownloadUrl_SignsExternalHttpsOriginAndForcesAttachment()
    {
        var options = Settings("https://downloads.example.test");
        using var internalClient = new AmazonS3Client(options.AccessKeyId, options.SecretAccessKey, new AmazonS3Config { ServiceURL = options.ServiceUrl, AuthenticationRegion = "garage" });
        var service = new S3ObjectStorageService(internalClient, Options.Create(options));
        var url = new Uri(await service.CreatePresignedDownloadUrlAsync("uploads/report file.pdf", TimeSpan.FromMinutes(5)));
        Assert.Equal("https", url.Scheme);
        Assert.Equal("downloads.example.test", url.Host);
        Assert.Equal("/equitylens/uploads/report%20file.pdf", url.AbsolutePath);
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal("300", query["X-Amz-Expires"].ToString());
        Assert.Equal("AWS4-HMAC-SHA256", query["X-Amz-Algorithm"].ToString());
        Assert.Equal("attachment", query["response-content-disposition"].ToString());
        Assert.Equal("application/octet-stream", query["response-content-type"].ToString());
        Assert.NotEmpty(query["X-Amz-Signature"].ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://garage:3900")]
    [InlineData("https://localhost")]
    [InlineData("https://garage")]
    [InlineData("https://192.168.1.2")]
    [InlineData("https://downloads.example.test/path")]
    [InlineData("https://downloads.example.test/?key=value")]
    [InlineData("https://user@downloads.example.test")]
    public async Task InvalidPublicEndpoint_NeverFallsBackToInternalHost(string endpoint)
    {
        var options = Settings(endpoint);
        using var internalClient = new AmazonS3Client(options.AccessKeyId, options.SecretAccessKey, new AmazonS3Config { ServiceURL = options.ServiceUrl, AuthenticationRegion = "garage" });
        var service = new S3ObjectStorageService(internalClient, Options.Create(options));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePresignedDownloadUrlAsync("report.pdf"));
    }

    private static ObjectStorageOptions Settings(string endpoint) => new()
    {
        ServiceUrl = "http://garage:3900", PublicServiceUrl = endpoint,
        AccessKeyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)),
        SecretAccessKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
    };
}
