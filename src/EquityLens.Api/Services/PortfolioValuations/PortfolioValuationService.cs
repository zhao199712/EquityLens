using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.PortfolioTransactions;
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
    private readonly ITransactionRepository _transactionRepository;
    private readonly IExchangeRateService _exchangeRateService;

    /// <summary>
    /// 初始化投資組合估值服務。
    /// </summary>
    /// <param name="currentUser">目前使用者內容。</param>
    /// <param name="portfolioRepository">投資組合儲存庫。</param>
    /// <param name="marketPriceRepository">市場價格儲存庫。</param>
    /// <param name="transactionRepository">交易紀錄儲存庫。</param>
    /// <param name="exchangeRateService">匯率服務。</param>
    public PortfolioValuationService(
        ICurrentUserContext currentUser,
        IPortfolioRepository portfolioRepository,
        IMarketPriceRepository marketPriceRepository,
        ITransactionRepository transactionRepository,
        IExchangeRateService exchangeRateService)
    {
        _currentUser = currentUser;
        _portfolioRepository = portfolioRepository;
        _marketPriceRepository = marketPriceRepository;
        _transactionRepository = transactionRepository;
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

    /// <inheritdoc />
    public async Task<Result<PortfolioValuationHistoryResponse>> GetValuationHistoryAsync(
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return Result<PortfolioValuationHistoryResponse>.Failure(
                "valuation.invalid_date_range", "'from' must be earlier than or equal to 'to'.");
        }

        var portfolio = await _portfolioRepository.GetDetailAsync(
            portfolioId,
            _currentUser.UserId,
            cancellationToken);

        if (portfolio is null)
        {
            return Result<PortfolioValuationHistoryResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        var transactions = await _transactionRepository.ListAsync(portfolioId, null, cancellationToken);
        var orderedTransactions = transactions
            .Where(x => x.TransactionDate <= to)
            .OrderBy(x => x.TransactionDate)
            .ThenBy(x => x.CreatedAtUtc)
            .ToList();

        var securityIds = orderedTransactions
            .Select(x => x.SecurityId)
            .Distinct()
            .ToList();

        if (securityIds.Count == 0)
        {
            return Result<PortfolioValuationHistoryResponse>.Success(new PortfolioValuationHistoryResponse(
                portfolio.Id,
                from,
                to,
                portfolio.BaseCurrency,
                BuildEmptyPoints(from, to)));
        }

        var priceBySecurity = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>();
        foreach (var securityId in securityIds)
        {
            var prices = await _marketPriceRepository.GetBySecurityAsync(securityId, from, to, cancellationToken);
            priceBySecurity[securityId] = prices;
        }

        var costCurrencyBySecurity = portfolio.Holdings
            .GroupBy(x => x.SecurityId)
            .ToDictionary(x => x.Key, x => x.First().CostCurrency);

        var lots = new Dictionary<Guid, RunningHolding>();
        var latestPriceBySecurity = new Dictionary<Guid, decimal>();
        var priceIndexBySecurity = securityIds.ToDictionary(x => x, _ => 0);
        var points = new List<PortfolioValuationHistoryPoint>();
        var txIndex = 0;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            while (txIndex < orderedTransactions.Count && orderedTransactions[txIndex].TransactionDate <= date)
            {
                ApplyTransaction(lots, orderedTransactions[txIndex]);
                txIndex++;
            }

            foreach (var securityId in securityIds)
            {
                var prices = priceBySecurity[securityId];
                var priceIndex = priceIndexBySecurity[securityId];
                while (priceIndex < prices.Count && DateOnly.FromDateTime(prices[priceIndex].PriceTime) <= date)
                {
                    latestPriceBySecurity[securityId] = prices[priceIndex].AdjustedClose ?? prices[priceIndex].Close;
                    priceIndex++;
                }

                priceIndexBySecurity[securityId] = priceIndex;
            }

            decimal totalCostValue = 0;
            decimal totalMarketValue = 0;
            var holdingCount = 0;
            var pricedHoldingCount = 0;

            foreach (var (securityId, holding) in lots.Where(x => x.Value.Quantity > 0))
            {
                holdingCount++;
                var currency = costCurrencyBySecurity.GetValueOrDefault(securityId, portfolio.BaseCurrency);
                var costInBase = await _exchangeRateService.ConvertAsync(
                    holding.CostValue, currency, portfolio.BaseCurrency, cancellationToken);
                totalCostValue += costInBase;

                if (!latestPriceBySecurity.TryGetValue(securityId, out var price))
                {
                    continue;
                }

                pricedHoldingCount++;
                var marketValue = holding.Quantity * price;
                var marketValueInBase = await _exchangeRateService.ConvertAsync(
                    marketValue, currency, portfolio.BaseCurrency, cancellationToken);
                totalMarketValue += marketValueInBase;
            }

            var totalUnrealizedPnl = totalMarketValue - totalCostValue;
            var totalUnrealizedPnlPercent = PortfolioMath.CalculateUnrealizedPnlPercent(totalUnrealizedPnl, totalCostValue);
            points.Add(new PortfolioValuationHistoryPoint(
                date,
                totalCostValue,
                totalMarketValue,
                totalUnrealizedPnl,
                totalUnrealizedPnlPercent,
                holdingCount,
                pricedHoldingCount));
        }

        return Result<PortfolioValuationHistoryResponse>.Success(new PortfolioValuationHistoryResponse(
            portfolio.Id,
            from,
            to,
            portfolio.BaseCurrency,
            points));
    }

    private static IReadOnlyList<PortfolioValuationHistoryPoint> BuildEmptyPoints(DateOnly from, DateOnly to)
    {
        var points = new List<PortfolioValuationHistoryPoint>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            points.Add(new PortfolioValuationHistoryPoint(date, 0, 0, 0, null, 0, 0));
        }

        return points;
    }

    private static void ApplyTransaction(Dictionary<Guid, RunningHolding> lots, Data.Entities.PortfolioTransaction tx)
    {
        if (!lots.TryGetValue(tx.SecurityId, out var holding))
        {
            holding = new RunningHolding(0, 0);
        }

        if (tx.TransactionType == "BUY")
        {
            holding = holding with
            {
                Quantity = holding.Quantity + tx.Quantity,
                CostValue = holding.CostValue + tx.Quantity * tx.Price + tx.Fee
            };
        }
        else if (tx.TransactionType == "SELL")
        {
            var averageCost = holding.Quantity <= 0 ? 0 : holding.CostValue / holding.Quantity;
            var sellQuantity = Math.Min(tx.Quantity, holding.Quantity);
            holding = holding with
            {
                Quantity = holding.Quantity - sellQuantity,
                CostValue = holding.CostValue - averageCost * sellQuantity
            };
        }

        lots[tx.SecurityId] = holding;
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

    private sealed record RunningHolding(decimal Quantity, decimal CostValue);
}
