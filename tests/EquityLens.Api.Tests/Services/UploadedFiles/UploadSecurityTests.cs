using Amazon.S3.Model;
using EquityLens.Api.Controllers;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.ObjectStorage;
using EquityLens.Api.Services.UploadedFiles;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace EquityLens.Api.Tests.Services.UploadedFiles;

public sealed class UploadSecurityTests
{
    [Theory]
    [InlineData("payload.html", "text/html", "<script>alert(1)</script>")]
    [InlineData("fake.pdf", "application/pdf", "<html>not a PDF</html>")]
    [InlineData("truncated.pdf", "application/pdf", "%PDF-1.7\nnot a valid document")]
    [InlineData("payload.pdf.exe", "application/pdf", "%PDF-1.7")]
    public async Task InvalidFile_IsRejectedBeforeStorage(string name, string mime, string content)
    {
        using var db = RefactorTestDbContext.Create();
        var storage = new RecordingStorage();
        var service = new UploadedFileService(db, new TestUser(), storage);
        var controller = new FilesController(service);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var file = new FormFile(stream, 0, stream.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = mime };
        var response = await controller.UploadFile(file, default);
        var result = Assert.IsAssignableFrom<ObjectResult>(response.Result);
        Assert.InRange(result.StatusCode!.Value, 400, 415);
        Assert.Equal(0, storage.Uploads);
        Assert.Empty(db.UploadedFiles);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/octet-stream")]
    public async Task ValidPdf_IsAcceptedAndStoredWithCanonicalMime(string mime)
    {
        using var db = RefactorTestDbContext.Create();
        var storage = new RecordingStorage();
        var service = new UploadedFileService(db, new TestUser(), storage);
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        builder.AddPage(100, 100);
        using var stream = new MemoryStream(builder.Build());
        var file = new FormFile(stream, 0, stream.Length, "file", "report.PDF") { Headers = new HeaderDictionary(), ContentType = mime };
        var result = await new FilesController(service).UploadFile(file, default);
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, storage.Uploads);
        Assert.Equal("application/pdf", Assert.Single(db.UploadedFiles).ContentType);
    }

    [Fact]
    public async Task OversizedPdf_IsRejectedBeforeReadingOrStorage()
    {
        using var db = RefactorTestDbContext.Create();
        var storage = new RecordingStorage();
        using var stream = new MemoryStream();
        var file = new FormFile(stream, 0, 20 * 1024 * 1024 + 1, "file", "large.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        var controller = new FilesController(new UploadedFileService(db, new TestUser(), storage));
        var result = Assert.IsAssignableFrom<ObjectResult>((await controller.UploadFile(file, default)).Result);
        Assert.Equal(413, result.StatusCode);
        Assert.Equal(0, storage.Uploads);
        Assert.Empty(db.UploadedFiles);
    }

    [Fact]
    public async Task FinancialFilingUpload_AlsoRejectsDisguisedHtml()
    {
        using var db = RefactorTestDbContext.Create();
        var storage = new RecordingStorage();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<html>not pdf</html>"));
        var file = new FormFile(stream, 0, stream.Length, "file", "fake.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        var service = new EquityLens.Api.Services.FinancialFilings.FinancialFilingService(db, new TestUser(), storage);
        var error = await Assert.ThrowsAsync<BadHttpRequestException>(() => service.UploadAsync(file, Guid.NewGuid(), 2026, null, "AnnualReport", null, null, null, null, null, null));
        Assert.Equal(415, error.StatusCode);
        Assert.Equal(0, storage.Uploads);
        Assert.Empty(db.UploadedFiles);
    }

    [Fact]
    public async Task DownloadUrl_DoesNotExposeOtherOwnersFile()
    {
        using var db = RefactorTestDbContext.Create();
        db.UploadedFiles.Add(new EquityLens.Api.Data.Entities.UploadedFile
        {
            Id = Guid.NewGuid(), UploadedByUserId = Guid.NewGuid(), OriginalFileName = "private.pdf", ObjectKey = "private-key", BucketName = "test", StorageProvider = "test", UploadStatus = "Uploaded"
        });
        await db.SaveChangesAsync();
        var service = new UploadedFileService(db, new TestUser(), new RecordingStorage());
        Assert.Null(await service.CreateDownloadUrlAsync(db.UploadedFiles.Single().Id));
    }

    private sealed class TestUser : ICurrentUserContext
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Email => "upload@example.test";
        public string DisplayName => "Upload test";
        public bool IsAuthenticated => true;
    }
    private sealed class RecordingStorage : IObjectStorageService
    {
        public int Uploads { get; private set; }
        public Task<PutObjectResponse> UploadAsync(string key, Stream content, string? mime, CancellationToken cancellationToken = default)
        { Uploads++; return Task.FromResult(new PutObjectResponse()); }
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> CreatePresignedDownloadUrlAsync(string key, TimeSpan? expiresIn = null, CancellationToken cancellationToken = default) => Task.FromResult("https://storage.example.test/file");
    }
}
