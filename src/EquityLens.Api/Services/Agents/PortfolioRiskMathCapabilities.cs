using System.Globalization;
using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Agents;

public interface IPortfolioRiskMathArguments
{
    JsonObject ToJson();
}

public sealed record NoPortfolioRiskMathArguments : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new();
}

public sealed record EwmaVolatilityArguments(decimal Lambda) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["lambda"] = Lambda };
}

public sealed record RollingLogReturnsArguments(int HorizonDays) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["horizonDays"] = HorizonDays };
}

public sealed record SharpeRatioArguments(decimal RiskFreeRate) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["riskFreeRate"] = RiskFreeRate };
}

public sealed record HistoricalVarArguments(decimal ConfidenceLevel) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["confidenceLevel"] = ConfidenceLevel };
}

public sealed record HistoricalExpectedShortfallArguments(decimal ConfidenceLevel) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["confidenceLevel"] = ConfidenceLevel };
}

public sealed record MonteCarloArguments(int HorizonDays, int Simulations, decimal ConfidenceLevel) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["horizonDays"] = HorizonDays, ["simulations"] = Simulations, ["confidenceLevel"] = ConfidenceLevel };
}

public sealed record CorrelatedGbmArguments(int HorizonDays, int Simulations, decimal ConfidenceLevel) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["horizonDays"] = HorizonDays, ["simulations"] = Simulations, ["confidenceLevel"] = ConfidenceLevel };
}

public sealed record MultivariateFhsArguments(decimal ConfidenceLevel, int HorizonDays, int Simulations, decimal Lambda, decimal ShrinkageAlpha) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["confidenceLevel"] = ConfidenceLevel, ["horizonDays"] = HorizonDays, ["simulations"] = Simulations, ["lambda"] = Lambda, ["shrinkageAlpha"] = ShrinkageAlpha };
}

public sealed record MultivariateFhsConfidenceLevelsArguments(decimal ConfidenceLevel, int HorizonDays, int Simulations, decimal Lambda, decimal ShrinkageAlpha) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["confidenceLevel"] = ConfidenceLevel, ["horizonDays"] = HorizonDays, ["simulations"] = Simulations, ["lambda"] = Lambda, ["shrinkageAlpha"] = ShrinkageAlpha };
}

public sealed record MultivariateFhsPathArguments(int HorizonDays, int Simulations, decimal Lambda, decimal ShrinkageAlpha) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["horizonDays"] = HorizonDays, ["simulations"] = Simulations, ["lambda"] = Lambda, ["shrinkageAlpha"] = ShrinkageAlpha };
}

public sealed record RightSkewArguments(decimal ExpectedMedianGap) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["expectedMedianGap"] = ExpectedMedianGap };
}

public sealed record MultivariateEwmaArguments(decimal Lambda) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["lambda"] = Lambda };
}

public sealed record DiagonalShrinkageArguments(decimal Lambda, decimal ShrinkageAlpha) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["lambda"] = Lambda, ["shrinkageAlpha"] = ShrinkageAlpha };
}

public sealed record AddJitterArguments(decimal Lambda) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["lambda"] = Lambda };
}

public sealed record FilteredResidualArguments(decimal Lambda) : IPortfolioRiskMathArguments
{
    public JsonObject ToJson() => new() { ["lambda"] = Lambda };
}

public sealed record PortfolioRiskMathCapabilityDefinition(
    NodeCapability Capability,
    Func<JsonObject, IPortfolioRiskMathArguments> ParseArguments);

public static class PortfolioRiskMathCapabilities
{
    private static readonly string[] ForbiddenSourceKeys = ["prices", "returns", "weights", "covarianceMatrix", "holdings", "initialValues"];

    public static IReadOnlyList<PortfolioRiskMathCapabilityDefinition> Definitions { get; } =
    [
        Empty("calculate-market-value", "Calculate market value from holding quantity and latest price.", "Portfolio", "MarketValueArguments"),
        Empty("calculate-cost-value", "Calculate holding cost value.", "Portfolio", "CostValueArguments"),
        Empty("calculate-unrealized-pnl", "Calculate unrealized profit or loss.", "Portfolio", "UnrealizedPnlArguments"),
        Empty("calculate-unrealized-pnl-percent", "Calculate unrealized profit or loss percentage.", "Portfolio", "UnrealizedPnlPercentArguments"),
        Empty("calculate-weight", "Calculate a holding's portfolio weight.", "Portfolio", "PortfolioWeightArguments"),
        Empty("calculate-return", "Calculate period return.", "SingleAsset", "PeriodReturnArguments"),
        Empty("calculate-portfolio-return", "Calculate weighted portfolio return.", "MultiAsset", "PortfolioReturnArguments"),
        Empty("calculate-concentration", "Calculate HHI and largest portfolio weight.", "MultiAsset", "ConcentrationArguments"),
        Empty("calculate-variance", "Calculate sample variance.", "SingleAsset", "VarianceArguments"),
        Empty("calculate-volatility", "Calculate daily volatility.", "SingleAsset", "VolatilityArguments"),
        Empty("calculate-annualized-volatility", "Calculate annualized volatility.", "SingleAsset", "AnnualizedVolatilityArguments"),
        Define("calculate-ewma-volatility", "Calculate EWMA volatility.", "SingleAsset", "EwmaVolatilityArguments", Schema(("lambda", Number(.80m, .999m, .94m))),
            args => new EwmaVolatilityArguments(Decimal(args, "lambda", .94m, .80m, .999m))),
        Define("calculate-rolling-log-returns", "Calculate rolling logarithmic returns.", "SingleAsset", "RollingLogReturnsArguments", Schema(("horizonDays", Integer(1, 252, 10))),
            args => new RollingLogReturnsArguments(Int32(args, "horizonDays", 10, 1, 252))),
        Define("calculate-sharpe-ratio", "Calculate annualized Sharpe ratio.", "SingleAsset", "SharpeRatioArguments", Schema(("riskFreeRate", Number(-1m, 1m, 0m))),
            args => new SharpeRatioArguments(Decimal(args, "riskFreeRate", 0m, -1m, 1m))),
        Empty("calculate-max-drawdown", "Calculate maximum historical drawdown.", "SingleAsset", "MaximumDrawdownArguments"),
        Define("calculate-historical-var", "Calculate historical Value at Risk.", "SingleAsset", "HistoricalVarArguments", ConfidenceSchema(),
            args => new HistoricalVarArguments(Decimal(args, "confidenceLevel", .95m, .90m, .999m))),
        Define("calculate-expected-shortfall", "Calculate historical Expected Shortfall.", "SingleAsset", "HistoricalExpectedShortfallArguments", ConfidenceSchema(),
            args => new HistoricalExpectedShortfallArguments(Decimal(args, "confidenceLevel", .95m, .90m, .999m))),
        Define("run-monte-carlo-simulation", "Run a reproducible single-asset GBM Monte Carlo simulation.", "SingleAsset", "MonteCarloArguments", SimulationSchema(),
            args => new MonteCarloArguments(Int32(args, "horizonDays", 10, 1, 252), Int32(args, "simulations", 10000, 1, 10000), Decimal(args, "confidenceLevel", .95m, .90m, .999m))),
        Empty("calculate-correlation", "Calculate Pearson correlation.", "MultiAsset", "CorrelationArguments"),
        Empty("calculate-covariance", "Calculate covariance.", "MultiAsset", "CovarianceArguments"),
        Empty("calculate-beta", "Calculate asset beta against the benchmark.", "MultiAsset", "BetaArguments"),
        Empty("calculate-portfolio-variance", "Calculate portfolio variance from weights and covariance.", "MultiAsset", "PortfolioVarianceArguments"),
        Empty("calculate-portfolio-volatility", "Calculate portfolio volatility.", "MultiAsset", "PortfolioVolatilityArguments"),
        Empty("calculate-volatility-risk-contribution", "Calculate component and incremental volatility risk contribution.", "MultiAsset", "VolatilityRiskContributionArguments"),
        Empty("estimate-gbm-parameters", "Estimate annualized GBM drift and volatility.", "SingleAsset", "GbmParameterEstimationArguments"),
        Empty("calculate-correlation-matrix", "Calculate a multi-asset correlation matrix.", "Matrix", "CorrelationMatrixArguments"),
        Empty("cholesky-decompose", "Calculate Cholesky decomposition for a positive-definite matrix.", "Matrix", "CholeskyDecompositionArguments"),
        Define("run-correlated-gbm-monte-carlo", "Run a reproducible correlated GBM Monte Carlo simulation.", "MultiAsset", "CorrelatedGbmArguments", SimulationSchema(),
            args => new CorrelatedGbmArguments(Int32(args, "horizonDays", 10, 1, 252), Int32(args, "simulations", 10000, 1, 10000), Decimal(args, "confidenceLevel", .95m, .90m, .999m))),
        Empty("determine-auto-shrinkage-alpha", "Determine covariance shrinkage alpha from data dimensions.", "Matrix", "AutoShrinkageAlphaArguments"),
        Define("run-multivariate-fhs-simulation", "Run reproducible multivariate EWMA filtered historical simulation.", "MultiAsset", "MultivariateFhsArguments", FhsSchema(includeConfidence: true),
            args => new MultivariateFhsArguments(Decimal(args, "confidenceLevel", .95m, .90m, .999m), Int32(args, "horizonDays", 10, 1, 252), Int32(args, "simulations", 10000, 1, 10000), Decimal(args, "lambda", .94m, .80m, .999m), Decimal(args, "shrinkageAlpha", .10m, 0m, .5m))),
        Define("run-multivariate-fhs-confidence-levels", "Run multivariate FHS for multiple confidence levels.", "MultiAsset", "MultivariateFhsConfidenceLevelsArguments", FhsSchema(includeConfidence: true),
            args => new MultivariateFhsConfidenceLevelsArguments(Decimal(args, "confidenceLevel", .95m, .90m, .999m), Int32(args, "horizonDays", 10, 1, 252), Int32(args, "simulations", 10000, 1, 10000), Decimal(args, "lambda", .94m, .80m, .999m), Decimal(args, "shrinkageAlpha", .10m, 0m, .5m))),
        Define("run-multivariate-fhs-path-simulation", "Run reproducible multivariate FHS path simulation.", "MultiAsset", "MultivariateFhsPathArguments", FhsSchema(includeConfidence: false),
            args => new MultivariateFhsPathArguments(Int32(args, "horizonDays", 10, 1, 252), Int32(args, "simulations", 10000, 1, 10000), Decimal(args, "lambda", .94m, .80m, .999m), Decimal(args, "shrinkageAlpha", .10m, 0m, .5m))),
        Define("has-material-right-skew", "Determine whether expected-versus-median gap is materially right-skewed.", "SingleAsset", "RightSkewArguments", Schema(("expectedMedianGap", Number(-100m, 100m, 0m))),
            args => new RightSkewArguments(Decimal(args, "expectedMedianGap", 0m, -100m, 100m))),
        Define("calculate-multivariate-ewma-covariances", "Calculate the EWMA covariance history.", "Matrix", "MultivariateEwmaArguments", Schema(("lambda", Number(.80m, .999m, .94m))),
            args => new MultivariateEwmaArguments(Decimal(args, "lambda", .94m, .80m, .999m))),
        Define("apply-diagonal-shrinkage", "Apply diagonal shrinkage to covariance matrices.", "Matrix", "DiagonalShrinkageArguments", Schema(("lambda", Number(.80m, .999m, .94m)), ("shrinkageAlpha", Number(0m, .5m, .10m))),
            args => new DiagonalShrinkageArguments(Decimal(args, "lambda", .94m, .80m, .999m), Decimal(args, "shrinkageAlpha", .10m, 0m, .5m))),
        Define("add-jitter", "Add numerical-stability jitter to covariance matrices.", "Matrix", "AddJitterArguments", Schema(("lambda", Number(.80m, .999m, .94m))),
            args => new AddJitterArguments(Decimal(args, "lambda", .94m, .80m, .999m))),
        Define("build-filtered-residual-vectors", "Build filtered residual vectors from returns and covariance history.", "Matrix", "FilteredResidualArguments", Schema(("lambda", Number(.80m, .999m, .94m))),
            args => new FilteredResidualArguments(Decimal(args, "lambda", .94m, .80m, .999m)))
    ];

    public static IReadOnlyList<NodeCapability> All { get; } = Definitions.Select(x => x.Capability).ToList();

    public static PortfolioRiskMathCapabilityDefinition GetDefinition(string id) =>
        Definitions.SingleOrDefault(x => x.Capability.Id == id)
        ?? throw new AgentNodeException("unknown_math_capability", AgentNodeErrorCategories.ValidationFailure, $"Unknown math capability '{id}'.", retryable: false);

    public static IPortfolioRiskMathArguments ParseArguments(string id, JsonObject arguments) =>
        GetDefinition(id).ParseArguments(arguments);

    private static PortfolioRiskMathCapabilityDefinition Empty(string id, string description, string mode, string contract) =>
        Define(id, description, mode, contract, Schema(), _ => new NoPortfolioRiskMathArguments());

    private static PortfolioRiskMathCapabilityDefinition Define(string id, string description, string mode, string contract, JsonObject schema, Func<JsonObject, IPortfolioRiskMathArguments> parser) =>
        new(new NodeCapability(id, PortfolioRiskMathNodeTypes.Execute, description, contract,
            [AgentBlackboardKeys.MathInputs], [AgentBlackboardKeys.MathResults], "ReadOnly", true, false, 1,
            schema, mode), args =>
        {
            ValidateKeys(args, schema["properties"]!.AsObject().Select(x => x.Key));
            return parser(args);
        });

    private static JsonObject ConfidenceSchema() => Schema(("confidenceLevel", Number(.90m, .999m, .95m)));
    private static JsonObject SimulationSchema() => Schema(
        ("confidenceLevel", Number(.90m, .999m, .95m)),
        ("horizonDays", Integer(1, 252, 10)),
        ("simulations", Integer(1, 10000, 10000)));
    private static JsonObject FhsSchema(bool includeConfidence)
    {
        var properties = new List<(string, JsonObject)>
        {
            ("horizonDays", Integer(1, 252, 10)), ("simulations", Integer(1, 10000, 10000)),
            ("lambda", Number(.80m, .999m, .94m)), ("shrinkageAlpha", Number(0m, .5m, .10m))
        };
        if (includeConfidence) properties.Insert(0, ("confidenceLevel", Number(.90m, .999m, .95m)));
        return Schema(properties.ToArray());
    }
    private static JsonObject Schema(params (string Name, JsonObject Definition)[] properties) => new()
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = new JsonObject(properties.Select(x => KeyValuePair.Create<string, JsonNode?>(x.Name, x.Definition)).ToArray())
    };
    private static JsonObject Number(decimal min, decimal max, decimal fallback) => new() { ["type"] = "number", ["minimum"] = min, ["maximum"] = max, ["default"] = fallback };
    private static JsonObject Integer(int min, int max, int fallback) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max, ["default"] = fallback };

    private static void ValidateKeys(JsonObject arguments, IEnumerable<string> allowedKeys)
    {
        var allowed = allowedKeys.ToHashSet(StringComparer.Ordinal);
        var sourceKey = arguments.Select(x => x.Key).FirstOrDefault(x => ForbiddenSourceKeys.Contains(x, StringComparer.OrdinalIgnoreCase));
        if (sourceKey is not null)
            throw new AgentNodeException("math_source_injection_forbidden", AgentNodeErrorCategories.ValidationFailure, $"Math source field '{sourceKey}' must come from Blackboard.", retryable: false);
        var unknown = arguments.Select(x => x.Key).FirstOrDefault(x => !allowed.Contains(x));
        if (unknown is not null)
            throw new AgentNodeException("math_parameter_unknown", AgentNodeErrorCategories.ValidationFailure, $"Math capability does not accept parameter '{unknown}'.", retryable: false);
    }

    private static decimal Decimal(JsonObject arguments, string key, decimal fallback, decimal min, decimal max)
    {
        if (arguments[key] is null) return fallback;
        if (!decimal.TryParse(arguments[key]!.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw InvalidType(key, "number");
        if (value < min || value > max) throw OutOfRange(key);
        return value;
    }

    private static int Int32(JsonObject arguments, string key, int fallback, int min, int max)
    {
        if (arguments[key] is null) return fallback;
        if (!decimal.TryParse(arguments[key]!.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric)
            || numeric != decimal.Truncate(numeric) || numeric < int.MinValue || numeric > int.MaxValue)
            throw InvalidType(key, "integer");
        var value = decimal.ToInt32(numeric);
        if (value < min || value > max) throw OutOfRange(key);
        return value;
    }

    private static AgentNodeException InvalidType(string key, string expected) =>
        new("math_parameter_invalid_type", AgentNodeErrorCategories.ValidationFailure, $"Math parameter '{key}' must be a JSON {expected}.", retryable: false);
    private static AgentNodeException OutOfRange(string key) =>
        new("math_parameter_out_of_range", AgentNodeErrorCategories.ValidationFailure, $"Math parameter '{key}' is out of range.", retryable: false);
}
