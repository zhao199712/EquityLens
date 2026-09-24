using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioTransactions;
using EquityLens.Api.Services.PortfolioTransactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 投資組合交易控制器，提供交易紀錄的查詢、建立與刪除功能。
/// </summary>
[Authorize]
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/transactions")]
public class PortfolioTransactionsController : ApiControllerBase
{
    private readonly ITransactionService _transactionService;

    public PortfolioTransactionsController(ITransactionService transactionService)
    {
        _transactionService = transactionService;
    }

    /// <summary>
    /// 取得指定投資組合的交易紀錄。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TransactionResponse>>> GetTransactions(
        Guid portfolioId,
        [FromQuery] Guid? securityId,
        CancellationToken cancellationToken)
    {
        var result = await _transactionService.ListAsync(portfolioId, securityId, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// 在指定投資組合中建立新的交易紀錄（買入或賣出）。
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TransactionResponse>> CreateTransaction(
        Guid portfolioId,
        CreateTransactionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _transactionService.CreateAsync(portfolioId, request, idempotencyKey, cancellationToken);
        if (!result.IsSuccess)
        {
            var error = new ApiError(result.ErrorCode!, result.ErrorMessage!);
            return result.ErrorCode!.EndsWith("not_found", StringComparison.Ordinal)
                ? NotFound(error)
                : result.ErrorCode.EndsWith("conflict", StringComparison.Ordinal)
                    ? Conflict(error)
                    : BadRequest(error);
        }

        if (result.Value!.WasIdempotentReplay)
            return Ok(result.Value.Transaction);

        return CreatedAtAction(nameof(GetTransactions), new { portfolioId }, result.Value.Transaction);
    }

    /// <summary>
    /// 刪除指定投資組合中的一筆交易紀錄，並重新彙總持倉。
    /// </summary>
    [HttpDelete("{transactionId:guid}")]
    public async Task<IActionResult> DeleteTransaction(
        Guid portfolioId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var result = await _transactionService.DeleteAsync(portfolioId, transactionId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }

    /// <summary>
    /// 刪除指定投資組合中某支股票的所有交易紀錄。
    /// </summary>
    [HttpDelete("by-security/{securityId:guid}")]
    public async Task<IActionResult> DeleteAllBySecurity(
        Guid portfolioId,
        Guid securityId,
        CancellationToken cancellationToken)
    {
        var result = await _transactionService.DeleteAllBySecurityAsync(portfolioId, securityId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }
}
