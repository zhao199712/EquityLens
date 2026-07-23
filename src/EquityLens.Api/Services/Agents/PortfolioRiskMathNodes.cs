using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using EquityLens.Api.Domain.Calculations;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed record MathAssetInput(string Ticker, string Currency, decimal Quantity, decimal AverageCost, IReadOnlyList<DateOnly> PriceDates, IReadOnlyList<decimal> Prices, IReadOnlyList<decimal> Returns);
public sealed record PortfolioRiskMathInputs(string Mode, Guid? PortfolioId, string? Ticker, DateOnly From, DateOnly To, IReadOnlyList<MathAssetInput> Assets, IReadOnlyList<decimal> Weights, IReadOnlyList<IReadOnlyList<decimal>> ReturnMatrix, decimal[][] CovarianceMatrix, IReadOnlyList<DateOnly> CommonTradingDates, IReadOnlyList<decimal> PortfolioValues, IReadOnlyList<decimal> PortfolioSimpleReturns, IReadOnlyList<decimal> PortfolioLogReturns, int CommonTradingDays, string Source, IReadOnlyList<string> Warnings);
public sealed record PortfolioRiskMathResult(string Operation, object? Value, string? Unit, object Parameters, object Provenance, IReadOnlyList<string> Warnings, int? Seed);
public sealed record PortfolioConcentrationMathResult(decimal Hhi, decimal LargestWeight);

public interface IPortfolioRiskMathInputProvider
{
    Task<PortfolioRiskMathInputs> PrepareAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken);
}

public sealed class PortfolioRiskMathInputProvider : IPortfolioRiskMathInputProvider
{
    public async Task<PortfolioRiskMathInputs> PrepareAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var request = board[AgentBlackboardKeys.ResearchRequest]?.Deserialize<EquityLens.Api.Contracts.Research.ResearchAskRequest>(AgentNodeJson.SerializerOptions);
        var portfolioId = request?.PortfolioId ?? ReadGuid(board[AgentBlackboardKeys.PortfolioId]);
        var to = ReadDate(board[AgentBlackboardKeys.DiagnosisTo]) ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var from = ReadDate(board[AgentBlackboardKeys.DiagnosisFrom]) ?? to.AddDays(-370);
        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        List<(string Ticker, string Currency, decimal Quantity, decimal AverageCost, Guid SecurityId)> requested;
        if (portfolioId.HasValue)
        {
            var portfolio = await context.DbContext.Portfolios.Include(x => x.Holdings).ThenInclude(x => x.Security).AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == portfolioId && x.OwnerUserId == context.Run.UserId, cancellationToken)
                ?? throw new AgentNodeException("portfolio_not_found", AgentNodeErrorCategories.PermanentFailure, "Portfolio was not found or is not owned by this user.", retryable: false);
            requested = portfolio.Holdings.Select(x => (x.Security.Ticker, x.Security.Currency, x.Quantity, x.AverageCost, x.SecurityId)).ToList();
        }
        else
        {
            var ticker = board[AgentBlackboardKeys.Ticker]?.GetValue<string>()?.Trim().ToUpperInvariant();
            var security = await context.DbContext.Securities.AsNoTracking().SingleOrDefaultAsync(x => x.Ticker == ticker, cancellationToken)
                ?? throw new AgentNodeException("security_not_found", AgentNodeErrorCategories.PermanentFailure, "Security was not found.", retryable: false);
            requested = [(security.Ticker, security.Currency, 1m, 0m, security.Id)];
        }

        var ids = requested.Select(x => x.SecurityId).ToList();
        var rows = await context.DbContext.MarketPrices.AsNoTracking()
            .Where(x => ids.Contains(x.SecurityId) && x.Interval == "1d" && x.PriceTime >= fromUtc && x.PriceTime <= toUtc)
            .OrderBy(x => x.PriceTime).Select(x => new { x.SecurityId, x.PriceTime, Price = x.AdjustedClose ?? x.Close, x.DataSource }).ToListAsync(cancellationToken);
        var warnings = new List<string>();
        var priceMaps = requested.Select(item =>
        {
            var prices = rows.Where(x => x.SecurityId == item.SecurityId && x.Price > 0)
                .GroupBy(x => DateOnly.FromDateTime(x.PriceTime)).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.PriceTime).First().Price);
            return new { Item = item, Prices = prices };
        }).ToList();
        foreach (var excluded in priceMaps.Where(x => x.Prices.Count < 2)) warnings.Add($"Excluded {excluded.Item.Ticker}: fewer than two price observations.");
        priceMaps = priceMaps.Where(x => x.Prices.Count >= 2).ToList();
        if (priceMaps.Count == 0) throw new AgentNodeException("math_data_unavailable", AgentNodeErrorCategories.ValidationFailure, "No sufficient market-price history is available for mathematics.");

        var commonDates = priceMaps.Select(x => x.Prices.Keys.AsEnumerable()).Aggregate((left, right) => left.Intersect(right)).OrderBy(x => x).TakeLast(253).ToList();
        if (commonDates.Count < 2) throw new AgentNodeException("math_common_dates_unavailable", AgentNodeErrorCategories.ValidationFailure, "Fewer than two common trading dates are available.");
        var assets = priceMaps.Select(x =>
        {
            var prices = commonDates.Select(date => x.Prices[date]).ToList();
            return new MathAssetInput(x.Item.Ticker, x.Item.Currency, x.Item.Quantity, x.Item.AverageCost, commonDates, prices, LogReturns(prices));
        }).ToList();
        var common = commonDates.Count - 1;
        var matrix = assets.Select(x => (IReadOnlyList<decimal>)x.Returns.ToList()).ToList();
        var marketValues = assets.Select(x => x.Quantity * x.Prices[^1]).ToList();
        var total = marketValues.Sum();
        var weights = marketValues.Select(x => total == 0 ? 0 : x / total).ToList();
        var covariance = Matrix(matrix);
        var portfolioValues = Enumerable.Range(0, commonDates.Count).Select(index => assets.Sum(asset => asset.Quantity * asset.Prices[index])).ToList();
        var portfolioSimpleReturns = Enumerable.Range(1, portfolioValues.Count - 1).Select(index => RiskMath.CalculateReturn(portfolioValues[index], portfolioValues[index - 1])).ToList();
        var portfolioLogReturns = LogReturns(portfolioValues);
        var currencies = assets.Select(x => x.Currency).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (currencies.Count > 1) warnings.Add($"Portfolio contains multiple currencies ({string.Join(", ", currencies)}); FX conversion was not applied.");
        if (common < 100) warnings.Add($"Only {common} common daily returns are available; statistical risk estimates may be unstable.");
        var source = string.Join(",", rows.Select(x => x.DataSource).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        return new(portfolioId.HasValue ? "Portfolio" : "SingleAsset", portfolioId, assets.Count == 1 ? assets[0].Ticker : null, commonDates[0], commonDates[^1], assets, weights, matrix, covariance, commonDates, portfolioValues, portfolioSimpleReturns, portfolioLogReturns, common, string.IsNullOrWhiteSpace(source) ? "MarketPrices" : source, warnings);
    }

    private static IReadOnlyList<decimal> LogReturns(IReadOnlyList<decimal> prices) => Enumerable.Range(1, prices.Count - 1).Select(i => (decimal)Math.Log((double)(prices[i] / prices[i - 1]))).ToList();
    private static decimal[][] Matrix(IReadOnlyList<IReadOnlyList<decimal>> values) => values.Select(x => values.Select(y => RiskMath.CalculateCovariance(x, y)).ToArray()).ToArray();
    private static Guid? ReadGuid(JsonNode? node) => node is null ? null : Guid.TryParse(node.ToString(), out var value) ? value : null;
    private static DateOnly? ReadDate(JsonNode? node) => node is null ? null : DateOnly.TryParse(node.ToString(), out var value) ? value : null;
}

public interface IPortfolioRiskMathExecutor
{
    PortfolioRiskMathResult Execute(string operation, PortfolioRiskMathInputs input, JsonObject arguments, Guid runId);
}

public sealed class PortfolioRiskMathExecutor : IPortfolioRiskMathExecutor
{
    public PortfolioRiskMathResult Execute(string operation, PortfolioRiskMathInputs input, JsonObject arguments, Guid runId)
    {
        var definition = PortfolioRiskMathCapabilities.GetDefinition(operation);
        if (definition.Capability.InputMode is "MultiAsset" or "Matrix" && input.Mode != "Portfolio") throw new AgentNodeException("math_portfolio_required", AgentNodeErrorCategories.ValidationFailure, $"Math capability '{operation}' requires portfolio inputs.", retryable: false);
        var parsed = definition.ParseArguments(arguments);
        T Args<T>() where T : class, IPortfolioRiskMathArguments => parsed as T
            ?? throw new InvalidOperationException($"Math capability '{operation}' has an invalid internal argument contract.");
        var seed = StableSeed(runId, operation);
        var a = input.Assets[0]; var isPortfolio = input.Mode == "Portfolio"; var returns = isPortfolio ? input.PortfolioSimpleReturns : a.Returns; var logReturns = isPortfolio ? input.PortfolioLogReturns : a.Returns; var matrix = input.ReturnMatrix; var weights = input.Weights;
        var latest = isPortfolio ? input.PortfolioValues[^1] : a.Prices[^1]; var start = isPortfolio ? input.PortfolioValues[0] : a.Prices[0]; var portfolioReturn = RiskMath.CalculateReturn(input.PortfolioValues[^1], input.PortfolioValues[0]);
        var annualizedReturn = returns.Count == 0 ? 0m : returns.Average() * 252m;
        var annualizedVolatility = RiskMath.CalculateAnnualizedVolatility(RiskMath.CalculateVolatility(returns));
        object? value = operation switch
        {
            "calculate-market-value" => PortfolioMath.CalculateMarketValue(a.Quantity, latest),
            "calculate-cost-value" => PortfolioMath.CalculateCostValue(a.Quantity, a.AverageCost),
            "calculate-unrealized-pnl" => PortfolioMath.CalculateUnrealizedPnl(a.Quantity * latest, a.Quantity * a.AverageCost),
            "calculate-unrealized-pnl-percent" => PortfolioMath.CalculateUnrealizedPnlPercent(a.Quantity * (latest - a.AverageCost), a.Quantity * a.AverageCost),
            "calculate-weight" => PortfolioMath.CalculateWeight(a.Quantity * latest, input.Assets.Sum(x => x.Quantity * x.Prices[^1])),
            "calculate-return" => RiskMath.CalculateReturn(latest, start),
            "calculate-portfolio-return" => portfolioReturn,
            "calculate-concentration" => CalculateConcentration(weights),
            "calculate-variance" => RiskMath.CalculateVariance(returns),
            "calculate-volatility" => RiskMath.CalculateVolatility(returns),
            "calculate-annualized-volatility" => RiskMath.CalculateAnnualizedVolatility(RiskMath.CalculateVolatility(returns)),
            "calculate-ewma-volatility" => RiskMath.CalculateEwmaVolatility(returns, Args<EwmaVolatilityArguments>().Lambda),
            "calculate-rolling-log-returns" => RiskMath.CalculateRollingLogReturns(logReturns, Args<RollingLogReturnsArguments>().HorizonDays),
            "calculate-sharpe-ratio" => RiskMath.CalculateSharpeRatio(isPortfolio ? annualizedReturn : portfolioReturn, Args<SharpeRatioArguments>().RiskFreeRate, annualizedVolatility),
            "calculate-max-drawdown" => RiskMath.CalculateMaxDrawdown(input.PortfolioValues),
            "calculate-historical-var" => RiskMath.CalculateHistoricalVaR(returns, Args<HistoricalVarArguments>().ConfidenceLevel),
            "calculate-expected-shortfall" => RiskMath.CalculateExpectedShortfall(returns, Args<HistoricalExpectedShortfallArguments>().ConfidenceLevel),
            "run-monte-carlo-simulation" => RunSingleSimulation(a, Args<MonteCarloArguments>(), seed),
            "calculate-correlation" => RiskMath.CalculateCorrelation(matrix[0], matrix.Count > 1 ? matrix[1] : matrix[0]),
            "calculate-covariance" => RiskMath.CalculateCovariance(matrix[0], matrix.Count > 1 ? matrix[1] : matrix[0]),
            "calculate-beta" => RiskMath.CalculateBeta(matrix[0], matrix.Count > 1 ? matrix[1] : matrix[0]),
            "calculate-portfolio-variance" => RiskMath.CalculatePortfolioVariance(weights, input.CovarianceMatrix),
            "calculate-portfolio-volatility" => RiskMath.CalculatePortfolioVolatility(weights, input.CovarianceMatrix),
            "calculate-volatility-risk-contribution" => RiskMath.CalculateVolatilityRiskContribution(weights, input.CovarianceMatrix, Enumerable.Range(0, weights.Count).ToArray()),
            "estimate-gbm-parameters" => RiskMath.EstimateGbmParameters(a.Prices),
            "calculate-correlation-matrix" => RiskMath.CalculateCorrelationMatrix(matrix),
            "cholesky-decompose" => RiskMath.CholeskyDecompose(input.CovarianceMatrix),
            "run-correlated-gbm-monte-carlo" => RunCorrelated(input, Args<CorrelatedGbmArguments>(), seed),
            "determine-auto-shrinkage-alpha" => RiskMath.DetermineAutoShrinkageAlpha(input.Assets.Count, input.CommonTradingDays),
            "run-multivariate-fhs-simulation" => RunFhs(matrix, weights, input.PortfolioValues[^1], Args<MultivariateFhsArguments>(), seed),
            "run-multivariate-fhs-confidence-levels" => RunFhsConfidenceLevels(matrix, weights, input.PortfolioValues[^1], Args<MultivariateFhsConfidenceLevelsArguments>(), seed),
            "run-multivariate-fhs-path-simulation" => RunFhsPath(matrix, weights, Args<MultivariateFhsPathArguments>(), seed),
            "has-material-right-skew" => RiskMath.HasMaterialRightSkew(Args<RightSkewArguments>().ExpectedMedianGap),
            "calculate-multivariate-ewma-covariances" => RiskMath.CalculateMultivariateEwmaCovariances(matrix, Args<MultivariateEwmaArguments>().Lambda),
            "apply-diagonal-shrinkage" => RiskMath.ApplyDiagonalShrinkage(RiskMath.CalculateMultivariateEwmaCovariances(matrix, Args<DiagonalShrinkageArguments>().Lambda), Args<DiagonalShrinkageArguments>().ShrinkageAlpha),
            "add-jitter" => RiskMath.AddJitter(RiskMath.CalculateMultivariateEwmaCovariances(matrix, Args<AddJitterArguments>().Lambda)),
            "build-filtered-residual-vectors" => RiskMath.BuildFilteredResidualVectors(matrix, RiskMath.AddJitter(RiskMath.CalculateMultivariateEwmaCovariances(matrix, Args<FilteredResidualArguments>().Lambda))),
            _ => throw new InvalidOperationException()
        };
        var unit = operation.Contains("value", StringComparison.Ordinal) || operation.Contains("pnl", StringComparison.Ordinal) ? "currency" : operation.Contains("return", StringComparison.Ordinal) || operation.Contains("volatility", StringComparison.Ordinal) || operation.Contains("var", StringComparison.Ordinal) || operation.Contains("shortfall", StringComparison.Ordinal) || operation.Contains("drawdown", StringComparison.Ordinal) ? "ratio" : null;
        return new(operation, value, unit, parsed.ToJson(), new { input.Mode, input.PortfolioId, input.Ticker, input.From, input.To, input.CommonTradingDays, input.Source, commonTradingDateFrom = input.CommonTradingDates.FirstOrDefault(), commonTradingDateTo = input.CommonTradingDates.LastOrDefault(), assets = input.Assets.Select(x => x.Ticker) }, input.Warnings, operation.StartsWith("run-", StringComparison.Ordinal) ? seed : null);
    }

    private static object RunSingleSimulation(MathAssetInput asset, MonteCarloArguments arguments, int seed)
    {
        var gbm = RiskMath.EstimateGbmParameters(asset.Prices) ?? new GbmParameters(0, 0, asset.Prices.Count, asset.Returns.Count, 252);
        return RiskMath.RunMonteCarloSimulation(asset.Prices[^1], gbm.AnnualizedDrift, gbm.AnnualizedVolatility, arguments.HorizonDays, arguments.Simulations, arguments.ConfidenceLevel, 252, seed);
    }
    private static PortfolioConcentrationMathResult CalculateConcentration(IReadOnlyList<decimal> weights)
    {
        var (hhi, largestWeight) = RiskMath.CalculateConcentration(weights);
        return new(hhi, largestWeight);
    }
    private static object RunCorrelated(PortfolioRiskMathInputs input, CorrelatedGbmArguments arguments, int seed)
    {
        var gbm = input.Assets.Select(x => RiskMath.EstimateGbmParameters(x.Prices)).ToList();
        return RiskMath.RunCorrelatedGbmMonteCarloSimulation(input.Assets.Select(x => x.Prices[^1]).ToList(), gbm.Select(x => x?.AnnualizedDrift ?? 0).ToList(), gbm.Select(x => x?.AnnualizedVolatility ?? 0).ToList(), input.Weights, RiskMath.CalculateCorrelationMatrix(input.ReturnMatrix), arguments.HorizonDays, arguments.Simulations, arguments.ConfidenceLevel, input.PortfolioValues[^1], input.Weights, 252, seed);
    }
    private static object RunFhs(IReadOnlyList<IReadOnlyList<decimal>> matrix, IReadOnlyList<decimal> weights, decimal initialValue, MultivariateFhsArguments arguments, int seed) =>
        RiskMath.RunMultivariateFhsSimulation(matrix, weights, initialValue, arguments.HorizonDays, arguments.Simulations, arguments.ConfidenceLevel, arguments.Lambda, arguments.ShrinkageAlpha, 252, 0m, seed);
    private static object RunFhsConfidenceLevels(IReadOnlyList<IReadOnlyList<decimal>> matrix, IReadOnlyList<decimal> weights, decimal initialValue, MultivariateFhsConfidenceLevelsArguments arguments, int seed) =>
        RiskMath.RunMultivariateFhsSimulationForConfidenceLevels(matrix, weights, initialValue, arguments.HorizonDays, arguments.Simulations, [arguments.ConfidenceLevel, .99m], arguments.Lambda, arguments.ShrinkageAlpha, 252, 0m, seed);
    private static object RunFhsPath(IReadOnlyList<IReadOnlyList<decimal>> matrix, IReadOnlyList<decimal> weights, MultivariateFhsPathArguments arguments, int seed) =>
        RiskMath.RunMultivariateFhsPathSimulation(matrix, weights, arguments.HorizonDays, arguments.Simulations, 10, seed, arguments.Lambda, arguments.ShrinkageAlpha);
    private static int StableSeed(Guid runId, string operation) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes($"{runId:N}:{operation}")), 0);
}

public sealed class PreparePortfolioRiskMathInputsNodeHandler(IPortfolioRiskMathInputProvider provider) : IAgentNodeHandler
{
    public string NodeType => PortfolioRiskMathNodeTypes.PrepareInputs;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var result = await provider.PrepareAsync(context, cancellationToken); var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        board[AgentBlackboardKeys.MathInputs] = JsonSerializer.SerializeToNode(result, AgentNodeJson.SerializerOptions); board[AgentBlackboardKeys.MathResults] ??= new JsonArray();
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Node.OutputJson = AgentNodeJson.Serialize(result);
    }
}

public sealed class ExecutePortfolioRiskMathNodeHandler(IPortfolioRiskMathExecutor executor) : IAgentNodeHandler
{
    public string NodeType => PortfolioRiskMathNodeTypes.Execute;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var input = board[AgentBlackboardKeys.MathInputs]?.Deserialize<PortfolioRiskMathInputs>(AgentNodeJson.SerializerOptions) ?? throw new AgentNodeException("math_inputs_missing", AgentNodeErrorCategories.ValidationFailure, "Math inputs are missing.");
        var args = JsonNode.Parse(string.IsNullOrWhiteSpace(context.Node.InputJson) ? "{}" : context.Node.InputJson)?.AsObject() ?? new JsonObject();
        var isBatch = args["operations"] is JsonArray;
        var operations = args["operations"] is JsonArray array ? array.Select(x => x!.GetValue<string>()).ToList() : [context.Node.TemplateNodeKey ?? args["operation"]?.GetValue<string>() ?? args["capability"]?.GetValue<string>() ?? throw new AgentNodeException("math_operation_missing", AgentNodeErrorCategories.ValidationFailure, "Math operation is required.")];
        if (operations.Count > 8 || operations.Distinct(StringComparer.Ordinal).Count() != operations.Count) throw new AgentNodeException("math_operation_budget_exceeded", AgentNodeErrorCategories.ValidationFailure, "At most eight distinct math operations are allowed.", retryable: false);
        var capabilityArguments = args.DeepClone().AsObject();
        capabilityArguments.Remove("operations"); capabilityArguments.Remove("operation"); capabilityArguments.Remove("capability");
        if (isBatch && capabilityArguments.Count > 0)
            throw new AgentNodeException("math_batch_parameter_unknown", AgentNodeErrorCategories.ValidationFailure, "Batch math nodes accept only the operations control field.", retryable: false);
        var results = operations.Select(operation => executor.Execute(operation, input, capabilityArguments, context.Run.Id)).ToList();
        var existing = board[AgentBlackboardKeys.MathResults] as JsonArray ?? new JsonArray(); foreach (var result in results) existing.Add(JsonSerializer.SerializeToNode(result, AgentNodeJson.SerializerOptions));
        board[AgentBlackboardKeys.MathResults] = existing; context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Node.OutputJson = AgentNodeJson.Serialize(results);
        return Task.CompletedTask;
    }
}
