using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Agents;

public static class PortfolioRiskMathCapabilities
{
    private static readonly JsonObject DefaultParameters = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["confidenceLevel"] = new JsonObject { ["type"] = "number", ["minimum"] = 0.90, ["maximum"] = 0.999, ["default"] = 0.95 },
            ["horizonDays"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 252, ["default"] = 10 },
            ["simulations"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 10000, ["default"] = 10000 },
            ["riskFreeRate"] = new JsonObject { ["type"] = "number", ["default"] = 0 },
            ["lambda"] = new JsonObject { ["type"] = "number", ["minimum"] = 0.80, ["maximum"] = 0.999, ["default"] = 0.94 },
            ["shrinkageAlpha"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 0.5, ["default"] = 0.10 }
        }
    };

    public static IReadOnlyList<NodeCapability> All { get; } =
    [
        P("calculate-market-value", "Calculate market value from holding quantity and latest price."),
        P("calculate-cost-value", "Calculate holding cost value."),
        P("calculate-unrealized-pnl", "Calculate unrealized profit or loss."),
        P("calculate-unrealized-pnl-percent", "Calculate unrealized profit or loss percentage."),
        P("calculate-weight", "Calculate a holding's portfolio weight."),
        U("calculate-return", "Calculate period return."),
        M("calculate-portfolio-return", "Calculate weighted portfolio return."),
        M("calculate-concentration", "Calculate HHI and largest portfolio weight."),
        U("calculate-variance", "Calculate sample variance."),
        U("calculate-volatility", "Calculate daily volatility."),
        U("calculate-annualized-volatility", "Calculate annualized volatility."),
        U("calculate-ewma-volatility", "Calculate EWMA volatility."),
        U("calculate-rolling-log-returns", "Calculate rolling logarithmic returns."),
        U("calculate-sharpe-ratio", "Calculate annualized Sharpe ratio."),
        U("calculate-max-drawdown", "Calculate maximum historical drawdown."),
        U("calculate-historical-var", "Calculate historical Value at Risk."),
        U("calculate-expected-shortfall", "Calculate historical Expected Shortfall."),
        U("run-monte-carlo-simulation", "Run a reproducible single-asset GBM Monte Carlo simulation."),
        M("calculate-correlation", "Calculate Pearson correlation."),
        M("calculate-covariance", "Calculate covariance."),
        M("calculate-beta", "Calculate asset beta against the benchmark."),
        M("calculate-portfolio-variance", "Calculate portfolio variance from weights and covariance."),
        M("calculate-portfolio-volatility", "Calculate portfolio volatility."),
        M("calculate-volatility-risk-contribution", "Calculate component and incremental volatility risk contribution."),
        U("estimate-gbm-parameters", "Estimate annualized GBM drift and volatility."),
        R("calculate-correlation-matrix", "Calculate a multi-asset correlation matrix."),
        R("cholesky-decompose", "Calculate Cholesky decomposition for a positive-definite matrix."),
        M("run-correlated-gbm-monte-carlo", "Run a reproducible correlated GBM Monte Carlo simulation."),
        R("determine-auto-shrinkage-alpha", "Determine covariance shrinkage alpha from data dimensions."),
        M("run-multivariate-fhs-simulation", "Run reproducible multivariate EWMA filtered historical simulation."),
        M("run-multivariate-fhs-confidence-levels", "Run multivariate FHS for multiple confidence levels."),
        M("run-multivariate-fhs-path-simulation", "Run reproducible multivariate FHS path simulation."),
        U("has-material-right-skew", "Determine whether expected-versus-median gap is materially right-skewed."),
        R("calculate-multivariate-ewma-covariances", "Calculate the EWMA covariance history."),
        R("apply-diagonal-shrinkage", "Apply diagonal shrinkage to covariance matrices."),
        R("add-jitter", "Add numerical-stability jitter to covariance matrices."),
        R("build-filtered-residual-vectors", "Build filtered residual vectors from returns and covariance history.")
    ];

    private static NodeCapability P(string id, string description) => C(id, description, "Portfolio");
    private static NodeCapability U(string id, string description) => C(id, description, "SingleAsset");
    private static NodeCapability M(string id, string description) => C(id, description, "MultiAsset");
    private static NodeCapability R(string id, string description) => C(id, description, "Matrix");
    private static NodeCapability C(string id, string description, string mode) => new(
        id, PortfolioRiskMathNodeTypes.Execute, description, "PortfolioRiskMathArguments",
        [AgentBlackboardKeys.MathInputs], [AgentBlackboardKeys.MathResults], "ReadOnly", true, false, 1,
        DefaultParameters.DeepClone().AsObject(), mode);
}
