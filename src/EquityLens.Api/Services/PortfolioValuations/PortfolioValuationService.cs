using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Data;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.PortfolioTransactions;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.PortfolioTransactions;
using EquityLens.Api.Services.PortfolioFunding;

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
    private readonly EquityLensDbContext _dbContext;
    private readonly IPortfolioFundingService _portfolioFundingService;
    private readonly IPortfolioBenchmarkService _benchmarkService;

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
        IExchangeRateService exchangeRateService,
        EquityLensDbContext dbContext,
        IPortfolioFundingService portfolioFundingService,
        IPortfolioBenchmarkService benchmarkService)
    {
        _currentUser = currentUser;
        _portfolioRepository = portfolioRepository;
        _marketPriceRepository = marketPriceRepository;
        _transactionRepository = transactionRepository;
        _exchangeRateService = exchangeRateService;
        _dbContext = dbContext;
        _portfolioFundingService = portfolioFundingService;
        _benchmarkService = benchmarkService;
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

        await EnsureImplicitFundingAsync(portfolioId, cancellationToken);

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
                    "MissingPrice",
                    holding.Sector,
                    holding.Industry));
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
                "Priced",
                holding.Sector,
                holding.Industry));
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
                x.ValuationStatus,
                x.Sector,
                x.Industry))
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var transactions = await _transactionRepository.ListAsync(portfolioId, null, cancellationToken);
        var fifo = FifoPortfolioCalculator.Calculate(transactions);
        var flows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            _dbContext.PortfolioCashFlows.Where(x => x.PortfolioId == portfolioId && x.EffectiveDate <= today && x.Status != "Skipped" && x.Status != "Scheduled"), cancellationToken);
        var cashBalance = CalculateCashBalance(transactions, flows);
        decimal? todayPnl = null;
        var history = await GetValuationHistoryAsync(portfolioId, today.AddDays(-1), today, cancellationToken);
        if (history.IsSuccess && history.Value!.Points.Count == 2) todayPnl = history.Value.Points[^1].DailyPnl;
        var firstActivity = transactions.Select(x => x.TransactionDate)
            .Concat(flows.Select(x => x.EffectiveDate))
            .DefaultIfEmpty(today)
            .Min();
        var performanceHistory = await GetValuationHistoryAsync(portfolioId, firstActivity, today, cancellationToken);
        var twr = performanceHistory.IsSuccess ? CalculateTwr(performanceHistory.Value!.Points) : null;
        var xirr = CalculateXirr(flows, totalMarketValue + cashBalance, today);

        var response = new PortfolioValuationResponse(
            portfolio.Id,
            today,
            portfolio.BaseCurrency,
            totalCostValue,
            totalMarketValue,
            totalUnrealizedPnl,
            totalUnrealizedPnlPercent,
            holdings,
            cashBalance,
            totalMarketValue + cashBalance,
            fifo.IsValid ? fifo.TotalRealizedPnl : 0m,
            todayPnl,
            twr,
            xirr);

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

        await EnsureImplicitFundingAsync(portfolioId, cancellationToken);

        var transactions = await _transactionRepository.ListAsync(portfolioId, null, cancellationToken);
        var cashFlows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            _dbContext.PortfolioCashFlows.Where(x => x.PortfolioId == portfolioId && x.EffectiveDate <= to && x.Status != "Skipped" && x.Status != "Scheduled"), cancellationToken);
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
        // 歷史區間可能從非交易日開始。必須自最早交易日讀取價格，才能在
        // 區間首日沿用前一個有效收盤價；否則週末／休市日會把持股市值錯算為 0。
        var priceHistoryStart = orderedTransactions.Min(x => x.TransactionDate);
        foreach (var securityId in securityIds)
        {
            var prices = await _marketPriceRepository.GetBySecurityAsync(securityId, priceHistoryStart, to, cancellationToken);
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
        var appliedTransactions = new List<Data.Entities.PortfolioTransaction>();
        // 交易會在第一個日期重播所有歷史資料；現金流也必須先帶入起始日前已生效的餘額，
        // 否則初始入金會被漏算，導致第一個淨值接近零而使報酬率失真。
        decimal cashBalance = cashFlows
            .Where(x => x.EffectiveDate < from)
            .Sum(SignedCashFlow);
        decimal realizedPnl = 0m;
        decimal? previousTotalAssetValue = null;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            // 現金流是當日開盤前可用的資金。尤其是系統推導的入金與買入
            // 同日發生時，必須先入帳，否則會短暫產生接近零／負值的淨值，
            // 進而將 TWR 與累積報酬率放大成不合理的數字。
            var dayFlows = cashFlows.Where(x => x.EffectiveDate == date).ToList();
            var externalCashFlow = 0m;
            foreach (var flow in dayFlows)
            {
                var signed = SignedCashFlow(flow);
                cashBalance += signed;
                if (flow.FlowType is "Deposit" or "Withdrawal") externalCashFlow += signed;
            }

            while (txIndex < orderedTransactions.Count && orderedTransactions[txIndex].TransactionDate <= date)
            {
                cashBalance += orderedTransactions[txIndex].TransactionType == "BUY"
                    ? -(orderedTransactions[txIndex].Quantity * orderedTransactions[txIndex].Price + orderedTransactions[txIndex].Fee)
                    : orderedTransactions[txIndex].Quantity * orderedTransactions[txIndex].Price - orderedTransactions[txIndex].Fee;
                ApplyTransaction(lots, orderedTransactions[txIndex]);
                appliedTransactions.Add(orderedTransactions[txIndex]);
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

            var fifoAsOf = FifoPortfolioCalculator.Calculate(appliedTransactions);
            realizedPnl = fifoAsOf.IsValid ? fifoAsOf.TotalRealizedPnl : 0m;
            decimal totalCostValue = 0;
            decimal totalMarketValue = 0;
            var holdingCount = 0;
            var pricedHoldingCount = 0;

            foreach (var (securityId, fifoLots) in fifoAsOf.LotsBySecurity.Where(x => x.Value.Sum(lot => lot.Quantity) > 0))
            {
                var holdingQuantity = fifoLots.Sum(x => x.Quantity);
                var holdingCost = fifoLots.Sum(x => x.CostValue);
                holdingCount++;
                var currency = costCurrencyBySecurity.GetValueOrDefault(securityId, portfolio.BaseCurrency);
                var costInBase = await _exchangeRateService.ConvertAsync(
                    holdingCost, currency, portfolio.BaseCurrency, cancellationToken);
                totalCostValue += costInBase;

                if (!latestPriceBySecurity.TryGetValue(securityId, out var price))
                {
                    continue;
                }

                pricedHoldingCount++;
                var marketValue = holdingQuantity * price;
                var marketValueInBase = await _exchangeRateService.ConvertAsync(
                    marketValue, currency, portfolio.BaseCurrency, cancellationToken);
                totalMarketValue += marketValueInBase;
            }

            var totalUnrealizedPnl = totalMarketValue - totalCostValue;
            var totalUnrealizedPnlPercent = PortfolioMath.CalculateUnrealizedPnlPercent(totalUnrealizedPnl, totalCostValue);
            var totalAssetValue = totalMarketValue + cashBalance;
            decimal? dailyPnl = previousTotalAssetValue.HasValue ? totalAssetValue - previousTotalAssetValue.Value - externalCashFlow : null;
            points.Add(new PortfolioValuationHistoryPoint(
                date,
                totalCostValue,
                totalMarketValue,
                totalUnrealizedPnl,
                totalUnrealizedPnlPercent,
                holdingCount,
                pricedHoldingCount,
                cashBalance,
                totalAssetValue,
                realizedPnl,
                externalCashFlow,
                dailyPnl));
            previousTotalAssetValue = totalAssetValue;
        }

        IReadOnlyList<BenchmarkPoint>? benchmark = null;
        try { benchmark = PortfolioPerformanceCalculator.NormalizeBenchmark(points.Select(x => x.Date).ToList(), await _benchmarkService.GetTotalReturnIndexAsync(from, to, cancellationToken)); } catch { }
        decimal? benchmarkReturn = benchmark?.LastOrDefault(x => x.NormalizedValue.HasValue)?.NormalizedValue is decimal normalized ? normalized / 100m - 1m : null;
        var twr = PortfolioPerformanceCalculator.CalculateTwr(points);
        var beta = benchmark is not null ? PortfolioPerformanceCalculator.CalculateBeta(points, benchmark) : null;
        var days = to.DayNumber - from.DayNumber;
        var jensenAlpha = twr.HasValue && benchmarkReturn.HasValue && beta.HasValue && days > 0
            ? PortfolioPerformanceCalculator.CalculateJensenAlpha(twr.Value, benchmarkReturn.Value, beta.Value, 0.02m, days)
            : null;
        return Result<PortfolioValuationHistoryResponse>.Success(new PortfolioValuationHistoryResponse(
            portfolio.Id,
            from,
            to,
            portfolio.BaseCurrency,
            points,
            twr,
            PortfolioPerformanceCalculator.CalculatePeriodXirr(points),
            benchmark,
            benchmarkReturn,
            twr.HasValue && benchmarkReturn.HasValue ? twr.Value - benchmarkReturn.Value : null,
            beta,
            jensenAlpha));
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

    private static decimal CalculateCashBalance(IEnumerable<Data.Entities.PortfolioTransaction> transactions, IEnumerable<Data.Entities.PortfolioCashFlow> flows)
        => transactions.Sum(x => x.TransactionType == "BUY" ? -(x.Quantity * x.Price + x.Fee) : x.Quantity * x.Price - x.Fee) + flows.Sum(SignedCashFlow);

    private static decimal SignedCashFlow(Data.Entities.PortfolioCashFlow flow)
        => flow.FlowType is "Withdrawal" or "Fee" or "DividendTax" ? -flow.Amount : flow.Amount;

    private static decimal? CalculateTwr(IReadOnlyList<PortfolioValuationHistoryPoint> points)
    {
        decimal cumulative = 1m;
        var hasReturn = false;
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1].TotalAssetValue;
            var current = points[index].TotalAssetValue;
            if (previous <= 0 || current < 0) continue;
            cumulative *= (current - points[index].ExternalCashFlow) / previous;
            hasReturn = true;
        }
        return hasReturn ? cumulative - 1m : null;
    }

    private static decimal? CalculateXirr(IReadOnlyList<Data.Entities.PortfolioCashFlow> flows, decimal terminalValue, DateOnly asOf)
    {
        var external = flows.Where(x => x.FlowType is "Deposit" or "Withdrawal")
            .Select(x => (Date: x.EffectiveDate, Amount: x.FlowType == "Deposit" ? -x.Amount : x.Amount))
            .ToList();
        if (terminalValue > 0) external.Add((asOf, terminalValue));
        return PortfolioPerformanceCalculator.CalculateXirr(external);
    }

    private async Task EnsureImplicitFundingAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        await _portfolioFundingService.RebuildImplicitFundingAsync(portfolioId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
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
        string ValuationStatus,
        string? Sector,
        string? Industry);

    private sealed record RunningHolding(decimal Quantity, decimal CostValue);
}
