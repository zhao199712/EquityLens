using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.ExchangeRates;

namespace EquityLens.Api.Services.PortfolioValuations;

/// <summary>
/// 投資組合估值服務實現，提供投資組合市場估值計算功能。
/// </summary>
public sealed class PortfolioValuationService : IPortfolioValuationService
{
    private const string DailyInterval = "1d";

    private readonly ICurrentUserContext _currentUser;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IMarketPriceRepository _marketPriceRepository;
    private readonly IExchangeRateService _exchangeRateService;

    /// <summary>
    /// 初始化投資組合估值服務。
    /// </summary>
    /// <param name="currentUser">目前使用者內容。</param>
    /// <param name="portfolioRepository">投資組合儲存庫。</param>
    /// <param name="marketPriceRepository">市場價格儲存庫。</param>
    /// <param name="exchangeRateService">匯率服務。</param>
    public PortfolioValuationService(
        ICurrentUserContext currentUser,
        IPortfolioRepository portfolioRepository,
        IMarketPriceRepository marketPriceRepository,
        IExchangeRateService exchangeRateService)
    {
        _currentUser = currentUser;
        _portfolioRepository = portfolioRepository;
        _marketPriceRepository = marketPriceRepository;
        _exchangeRateService = exchangeRateService;
    }

    /// <inheritdoc />
    public async Task<Result<PortfolioValuationResponse>> GetValuationAsync(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetDetailAsync(
            portfolioId,
            _currentUser.UserId,
            cancellationToken);

        if (portfolio is null)
        {
            return Result<PortfolioValuationResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        var securityIds = portfolio.Holdings
            .Select(x => x.SecurityId)
            .Distinct()
            .ToList();

        // 取得各證券最新收盤價
        var latestPrices = await _marketPriceRepository.GetLatestPricesAsync(
            securityIds,
            DailyInterval,
            cancellationToken);

        // 逐筆計算持倉的市值、損益與估值狀態，並換算成 BaseCurrency
        var holdingDrafts = new List<HoldingValuationDraft>();
        foreach (var holding in portfolio.Holdings)
        {
            var costValue = PortfolioMath.CalculateCostValue(holding.Quantity, holding.AverageCost);

            // 將成本換算成 BaseCurrency
            var costValueInBase = await _exchangeRateService.ConvertAsync(
                costValue, holding.CostCurrency, portfolio.BaseCurrency, cancellationToken);

            if (!latestPrices.TryGetValue(holding.SecurityId, out var latestPrice))
            {
                holdingDrafts.Add(new HoldingValuationDraft(
                    holding.Id,
                    holding.SecurityId,
                    holding.Ticker,
                    holding.Exchange,
                    holding.SecurityName,
                    holding.Quantity,
                    holding.AverageCost,
                    holding.CostCurrency,
                    null,
                    null,
                    costValueInBase,
                    null,
                    null,
                    null,
                    "MissingPrice"));
                continue;
            }

            var marketValue = PortfolioMath.CalculateMarketValue(holding.Quantity, latestPrice.Price);
            var marketValueInBase = await _exchangeRateService.ConvertAsync(
                marketValue, holding.CostCurrency, portfolio.BaseCurrency, cancellationToken);

            var unrealizedPnl = PortfolioMath.CalculateUnrealizedPnl(marketValueInBase, costValueInBase);
            var unrealizedPnlPercent = PortfolioMath.CalculateUnrealizedPnlPercent(unrealizedPnl, costValueInBase);

            holdingDrafts.Add(new HoldingValuationDraft(
                holding.Id,
                holding.SecurityId,
                holding.Ticker,
                holding.Exchange,
                holding.SecurityName,
                holding.Quantity,
                holding.AverageCost,
                holding.CostCurrency,
                latestPrice.Price,
                latestPrice.PriceTime,
                costValueInBase,
                marketValueInBase,
                unrealizedPnl,
                unrealizedPnlPercent,
                "Priced"));
        }

        // 彙總投資組合層級的數據
        var totalCostValue = holdingDrafts.Sum(x => x.CostValue);
        var totalMarketValue = holdingDrafts.Sum(x => x.MarketValue ?? 0);
        var pricedCostValue = holdingDrafts.Where(x => x.MarketValue.HasValue).Sum(x => x.CostValue);
        var totalUnrealizedPnl = holdingDrafts.Sum(x => x.UnrealizedPnl ?? 0);
        var totalUnrealizedPnlPercent = PortfolioMath.CalculateUnrealizedPnlPercent(totalUnrealizedPnl, pricedCostValue);

        // 計算各持倉權重並轉換為回應物件
        var holdings = holdingDrafts
            .Select(x => new HoldingValuationResponse(
                x.HoldingId,
                x.SecurityId,
                x.Ticker,
                x.Exchange,
                x.SecurityName,
                x.Quantity,
                x.AverageCost,
                x.CostCurrency,
                x.LatestPrice,
                x.PriceTime,
                x.CostValue,
                x.MarketValue,
                x.UnrealizedPnl,
                x.UnrealizedPnlPercent,
                x.MarketValue.HasValue ? PortfolioMath.CalculateWeight(x.MarketValue.Value, totalMarketValue) : null,
                x.ValuationStatus))
            .ToList();

        var response = new PortfolioValuationResponse(
            portfolio.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            portfolio.BaseCurrency,
            totalCostValue,
            totalMarketValue,
            totalUnrealizedPnl,
            totalUnrealizedPnlPercent,
            holdings);

        return Result<PortfolioValuationResponse>.Success(response);
    }

    // 持倉估值計算的中間草稿記錄，用於彙總前暫存各持倉計算結果
    private sealed record HoldingValuationDraft(
        Guid HoldingId,
        Guid SecurityId,
        string Ticker,
        string Exchange,
        string SecurityName,
        decimal Quantity,
        decimal AverageCost,
        string CostCurrency,
        decimal? LatestPrice,
        DateTime? PriceTime,
        decimal CostValue,
        decimal? MarketValue,
        decimal? UnrealizedPnl,
        decimal? UnrealizedPnlPercent,
        string ValuationStatus);
}
