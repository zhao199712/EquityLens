using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioDividends;
using EquityLens.Api.Data;
using EquityLens.Api.Repositories.Portfolios;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.PortfolioDividends;

public sealed class PortfolioDividendService : IPortfolioDividendService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IPortfolioRepository _portfolioRepository;

    public PortfolioDividendService(
        EquityLensDbContext dbContext,
        IPortfolioRepository portfolioRepository)
    {
        _dbContext = dbContext;
        _portfolioRepository = portfolioRepository;
    }

    public async Task<Result<IReadOnlyList<LatestDividendResponse>>> GetLatestDividendsAsync(
        Guid portfolioId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, userId, cancellationToken))
            return Result<IReadOnlyList<LatestDividendResponse>>.Failure("portfolio.not_found", "Portfolio was not found.");

        var portfolio = await _portfolioRepository.GetDetailAsync(portfolioId, userId, cancellationToken);
        if (portfolio is null)
            return Result<IReadOnlyList<LatestDividendResponse>>.Failure("portfolio.not_found", "Portfolio was not found.");

        var securityIds = portfolio.Holdings.Select(h => h.SecurityId).Distinct().ToList();
        if (securityIds.Count == 0)
            return Result<IReadOnlyList<LatestDividendResponse>>.Success(Array.Empty<LatestDividendResponse>());

        var events = await _dbContext.CashDividendEvents
            .AsNoTracking()
            .Where(e => securityIds.Contains(e.SecurityId))
            .Include(e => e.Security)
            .ToListAsync(cancellationToken);

        var holdingBySecurity = portfolio.Holdings.ToDictionary(h => h.SecurityId);

        var latest = events
            .GroupBy(e => e.SecurityId)
            .Select(g => g
                .OrderByDescending(e => e.ExDividendDate)
                .ThenByDescending(e => e.PaymentDate ?? DateOnly.MinValue)
                .First())
            .OrderByDescending(e => e.ExDividendDate)
            .ThenByDescending(e => e.PaymentDate ?? DateOnly.MinValue)
            .Select(e =>
            {
                holdingBySecurity.TryGetValue(e.SecurityId, out var holding);
                return new LatestDividendResponse(
                    e.SecurityId,
                    e.Security.Ticker,
                    e.Security.Name,
                    e.ExDividendDate,
                    e.PaymentDate,
                    e.CashAmountPerShare,
                    e.Currency);
            })
            .ToList();

        return Result<IReadOnlyList<LatestDividendResponse>>.Success(latest);
    }

    public async Task<Result<IReadOnlyList<DividendCashFlowResponse>>> GetCashFlowsAsync(
        Guid portfolioId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, userId, cancellationToken))
            return Result<IReadOnlyList<DividendCashFlowResponse>>.Failure("portfolio.not_found", "Portfolio was not found.");

        var flows = await _dbContext.PortfolioCashFlows
            .AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.FlowType == "Dividend")
            .Include(x => x.Security)
            .Include(x => x.CashDividendEvent)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Result<IReadOnlyList<DividendCashFlowResponse>>.Success(
            flows.Select(x => ToResponse(x, today)).ToList());
    }

    public async Task<Result<DividendCashFlowResponse>> UpdateCashFlowAsync(
        Guid portfolioId, Guid cashFlowId, Guid userId, UpdateDividendCashFlowRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, userId, cancellationToken))
            return Result<DividendCashFlowResponse>.Failure("portfolio.not_found", "Portfolio was not found.");

        var flow = await _dbContext.PortfolioCashFlows
            .Include(x => x.Security)
            .Include(x => x.CashDividendEvent)
            .SingleOrDefaultAsync(x => x.Id == cashFlowId && x.PortfolioId == portfolioId && x.FlowType == "Dividend", cancellationToken);
        if (flow is null)
            return Result<DividendCashFlowResponse>.Failure("dividend.cash_flow_not_found", "Dividend cash flow was not found.");
        if (request.Amount is < 0)
            return Result<DividendCashFlowResponse>.Failure("dividend.invalid_amount", "Amount must not be negative.");

        if (request.Amount.HasValue)
        {
            flow.Amount = request.Amount.Value;
            flow.IsUserAdjusted = true;
        }
        if (request.Note is not null) flow.Note = request.Note;
        if (request.Skip.HasValue)
            flow.Status = request.Skip.Value ? "Skipped" : AutoStatus(flow.EffectiveDate);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<DividendCashFlowResponse>.Success(ToResponse(flow, DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    private static DividendCashFlowResponse ToResponse(Data.Entities.PortfolioCashFlow flow, DateOnly today)
    {
        var dividend = flow.CashDividendEvent!;
        return new DividendCashFlowResponse(
            flow.Id, flow.SecurityId ?? Guid.Empty, flow.Security?.Ticker ?? "—", flow.Security?.Name ?? "—",
            dividend.ExDividendDate, dividend.PaymentDate, dividend.CashAmountPerShare == 0 ? 0 : flow.Amount / dividend.CashAmountPerShare,
            dividend.CashAmountPerShare, flow.Amount, flow.Currency,
            flow.Status == "Skipped" ? "Skipped" : (flow.EffectiveDate <= today ? "Posted" : "Scheduled"),
            flow.IsUserAdjusted, flow.Note);
    }

    private static string AutoStatus(DateOnly effectiveDate) =>
        effectiveDate <= DateOnly.FromDateTime(DateTime.UtcNow) ? "Posted" : "Scheduled";
}
