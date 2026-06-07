using EquityLens.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// API 控制器基底類別，提供統一的 <see cref="Result{T}"/u003e 轉換為 ActionResult 的輔助方法。
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// 將 <see cref="Result{T}"/u003e 轉換為對應的 <see cref="ActionResult{T}"/u003e。
    /// 成功時返回 200 OK；錯誤碼以 "not_found" 結尾時返回 404 NotFound，其餘返回 400 BadRequest。
    /// </summary>
    /// <typeparam name="T">結果值的類型。</typeparam>
    /// <param name="result">要轉換的結果物件。</param>
    /// <returns>對應的 HTTP ActionResult。</returns>
    protected ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var error = new ApiError(result.ErrorCode!, result.ErrorMessage!);
        return result.ErrorCode!.EndsWith("not_found", StringComparison.Ordinal)
            ? NotFound(error)
            : BadRequest(error);
    }

    /// <summary>
    /// 將錯誤的 <see cref="Result{T}"/u003e 轉換為對應的 <see cref="IActionResult"/u003e（無返回值）。
    /// 錯誤碼以 "not_found" 結尾時返回 404 NotFound，其餘返回 400 BadRequest。
    /// </summary>
    /// <typeparam name="T">結果值的類型。</typeparam>
    /// <param name="result">要轉換的結果物件。</param>
    /// <returns>對應的 HTTP IActionResult。</returns>
    protected IActionResult ToErrorActionResult<T>(Result<T> result)
    {
        var error = new ApiError(result.ErrorCode!, result.ErrorMessage!);
        return result.ErrorCode!.EndsWith("not_found", StringComparison.Ordinal)
            ? NotFound(error)
            : BadRequest(error);
    }
}
