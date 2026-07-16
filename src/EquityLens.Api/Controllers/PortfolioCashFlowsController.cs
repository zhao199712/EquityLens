using EquityLens.Api.Contracts.PortfolioCashFlows;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/cash-flows")]
public sealed class PortfolioCashFlowsController : ControllerBase
{
    private readonly EquityLensDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    public PortfolioCashFlowsController(EquityLensDbContext db, ICurrentUserContext currentUser) { _db = db; _currentUser = currentUser; }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CashFlowResponse>>> List(Guid portfolioId, CancellationToken ct)
    {
        if (!await OwnsPortfolio(portfolioId, ct)) return NotFound();
        return Ok(await _db.PortfolioCashFlows.Where(x => x.PortfolioId == portfolioId)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => ToResponse(x)).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<CashFlowResponse>> Create(Guid portfolioId, UpsertCashFlowRequest request, CancellationToken ct)
    {
        if (!await OwnsPortfolio(portfolioId, ct)) return NotFound();
        if (request.FlowType is not ("Deposit" or "Withdrawal" or "Fee" or "DividendTax") || request.Amount <= 0)
            return BadRequest(new { code = "cash_flow.invalid", message = "Flow type or amount is invalid." });
        var flow = new PortfolioCashFlow { PortfolioId = portfolioId, FlowType = request.FlowType, Amount = request.Amount, Currency = "TWD", EffectiveDate = request.EffectiveDate, Status = "Posted", IsUserAdjusted = true, Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim() };
        _db.PortfolioCashFlows.Add(flow);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { portfolioId }, ToResponse(flow));
    }

    [HttpPut("{cashFlowId:guid}")]
    public async Task<ActionResult<CashFlowResponse>> Update(Guid portfolioId, Guid cashFlowId, UpsertCashFlowRequest request, CancellationToken ct)
    {
        if (!await OwnsPortfolio(portfolioId, ct)) return NotFound();
        var flow = await _db.PortfolioCashFlows.SingleOrDefaultAsync(x => x.PortfolioId == portfolioId && x.Id == cashFlowId, ct);
        if (flow is null) return NotFound();
        if (IsSystemDerived(flow) || flow.CashDividendEventId is not null || request.FlowType is not ("Deposit" or "Withdrawal" or "Fee" or "DividendTax") || request.Amount <= 0)
            return BadRequest(new { code = "cash_flow.not_editable", message = "Only manual cash flows can be edited here." });
        flow.FlowType = request.FlowType; flow.Amount = request.Amount; flow.EffectiveDate = request.EffectiveDate; flow.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(); flow.IsUserAdjusted = true;
        await _db.SaveChangesAsync(ct); return Ok(ToResponse(flow));
    }

    [HttpDelete("{cashFlowId:guid}")]
    public async Task<IActionResult> Delete(Guid portfolioId, Guid cashFlowId, CancellationToken ct)
    {
        if (!await OwnsPortfolio(portfolioId, ct)) return NotFound();
        var flow = await _db.PortfolioCashFlows.SingleOrDefaultAsync(x => x.PortfolioId == portfolioId && x.Id == cashFlowId, ct);
        if (flow is null) return NotFound();
        if (IsSystemDerived(flow) || flow.CashDividendEventId is not null) return BadRequest(new { code = "cash_flow.not_deletable", message = "System-derived and dividend cash flows cannot be deleted." });
        _db.PortfolioCashFlows.Remove(flow); await _db.SaveChangesAsync(ct); return NoContent();
    }

    private Task<bool> OwnsPortfolio(Guid id, CancellationToken ct) => _db.Portfolios.AnyAsync(x => x.Id == id && x.OwnerUserId == _currentUser.UserId && x.IsActive, ct);
    private static bool IsSystemDerived(PortfolioCashFlow x) => x.IsSystemDerived;
    private static CashFlowResponse ToResponse(PortfolioCashFlow x) => new(x.Id, x.FlowType, x.Amount, x.Currency, x.EffectiveDate, x.Status, x.IsUserAdjusted, x.Note, x.SecurityId, x.IsSystemDerived);
}
