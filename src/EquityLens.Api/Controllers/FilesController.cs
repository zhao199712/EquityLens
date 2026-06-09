using EquityLens.Api.Contracts.Files;
using EquityLens.Api.Services.UploadedFiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 檔案控制器，提供檔案上傳、查詢、取得下載 URL 與刪除功能。
/// </summary>
[Authorize]
[ApiController]
[Route("api/files")]
public sealed class FilesController : ControllerBase
{
    private readonly IUploadedFileService _uploadedFileService;

    /// <summary>
    /// 初始化檔案控制器。
    /// </summary>
    /// <param name="uploadedFileService">已上傳檔案服務。</param>
    public FilesController(IUploadedFileService uploadedFileService)
    {
        _uploadedFileService = uploadedFileService;
    }

    /// <summary>
    /// 上傳單一檔案到物件儲存。
    /// </summary>
    /// <param name="file">要上傳的表單檔案。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>成功時返回 200 OK 與檔案回應資料。</returns>
    [HttpPost]
    public async Task<ActionResult<UploadedFileResponse>> UploadFile(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "file_required", message = "請選擇要上傳的檔案。" });
        }

        var response = await _uploadedFileService.UploadAsync(file, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// 取得當前使用者的所有已上傳檔案列表。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>檔案回應列表。</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UploadedFileResponse>>> ListFiles(CancellationToken cancellationToken)
    {
        var files = await _uploadedFileService.ListAsync(cancellationToken);
        return Ok(files);
    }

    /// <summary>
    /// 依據檔案識別碼取得檔案記錄。
    /// </summary>
    /// <param name="id">檔案的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>檔案回應資料；若不存在則返回 404。</returns>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UploadedFileResponse>> GetFile(Guid id, CancellationToken cancellationToken)
    {
        var file = await _uploadedFileService.GetAsync(id, cancellationToken);
        return file is null ? NotFound() : Ok(file);
    }

    /// <summary>
    /// 產生指定檔案的限時下載 URL。
    /// </summary>
    /// <param name="id">檔案的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>Presigned URL 回應；若檔案不存在則返回 404。</returns>
    [HttpGet("{id:guid}/download-url")]
    public async Task<ActionResult<PresignedUrlResponse>> GetDownloadUrl(Guid id, CancellationToken cancellationToken)
    {
        var urlResponse = await _uploadedFileService.CreateDownloadUrlAsync(id, cancellationToken);
        return urlResponse is null ? NotFound() : Ok(urlResponse);
    }

    /// <summary>
    /// 刪除指定檔案的物件儲存資料與資料庫記錄。
    /// </summary>
    /// <param name="id">檔案的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>成功刪除返回 204 NoContent；若不存在則返回 404。</returns>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFile(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _uploadedFileService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
