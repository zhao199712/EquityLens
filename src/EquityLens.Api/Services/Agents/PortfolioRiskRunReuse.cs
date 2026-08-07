using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public static class PortfolioRiskEvidenceSources
{
    public const string PersistedRiskRun = "PersistedRiskRun";
    public const string WorkflowCalculation = "WorkflowCalculation";
    public const string Mixed = "Mixed";
    public const string CacheMiss = "CacheMiss";
}

public sealed record PortfolioRiskRunRejection(Guid RiskRunId, string Code, string Reason);
public sealed record PortfolioRiskEvidenceSnapshot(
    IReadOnlyList<Guid> SourceRiskRunIds,
    string Source,
    string CacheStatus,
    DateOnly? DataAsOfDate,
    string? InputHash,
    string? AlgorithmVersion,
    string? DataFactorVersion,
    IReadOnlyList<string> ReusedCapabilities,
    IReadOnlyList<string> CalculatedCapabilities,
    IReadOnlyList<PortfolioRiskRunRejection> RejectedRuns,
    bool CoreCalculationRequired);

public interface IPortfolioRiskRunResolver
{
    Task<(PortfolioRiskEvidenceSnapshot Evidence, IReadOnlyList<JsonObject> Results)> ResolveAsync(
        PortfolioDiagnosisContext diagnosis, Guid userId, CancellationToken cancellationToken);
}

public sealed class PortfolioRiskRunResolver(
    EquityLens.Api.Data.EquityLensDbContext db,
    IRiskBacktestInputProvider inputProvider) : IPortfolioRiskRunResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<(PortfolioRiskEvidenceSnapshot Evidence, IReadOnlyList<JsonObject> Results)> ResolveAsync(
        PortfolioDiagnosisContext diagnosis, Guid userId, CancellationToken cancellationToken)
    {
        var candidates = await db.RiskCalculationRuns.AsNoTracking()
            .Where(x => x.PortfolioId == diagnosis.PortfolioId && x.RequestedByUserId == userId
                && x.Operation == "risk" && x.Status == "Completed" && x.ResultJson != null)
            .OrderByDescending(x => x.CompletedAtUtc)
            .Take(10)
            .ToListAsync(cancellationToken);
        var rejected = new List<PortfolioRiskRunRejection>();
        RiskBacktestEngineInput? currentInput = null;

        foreach (var run in candidates)
        {
            if (!TrySnapshot(run.InputSnapshotJson, out var snapshot))
            {
                rejected.Add(new(run.Id, "INPUT_SNAPSHOT_INVALID", "Risk Run input snapshot is invalid."));
                continue;
            }
            if (snapshot.From != diagnosis.From || snapshot.To != diagnosis.To)
            {
                rejected.Add(new(run.Id, "DATE_RANGE_MISMATCH", "Risk Run date range differs from the diagnosis range."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(run.InputHash))
            {
                rejected.Add(new(run.Id, "INPUT_HASH_MISSING", "Risk Run does not contain a reusable input hash."));
                continue;
            }
            if (currentInput is null)
            {
                var prepared = await inputProvider.PreparePortfolioRiskBacktestInputAsync(
                    diagnosis.PortfolioId, diagnosis.From, diagnosis.To, userId, cancellationToken);
                if (!prepared.IsSuccess || prepared.Value is null)
                    return Miss(rejected, "CURRENT_INPUT_UNAVAILABLE", prepared.ErrorMessage ?? "Current risk input is unavailable.");
                currentInput = prepared.Value with { Simulations = snapshot.Simulations };
            }
            var currentHash = ComputeInputHash(currentInput);
            if (!string.Equals(currentHash, run.InputHash, StringComparison.OrdinalIgnoreCase))
            {
                rejected.Add(new(run.Id, "INPUT_HASH_MISMATCH", "Portfolio holdings, weights, prices, or risk inputs changed."));
                continue;
            }
            if (!PortfolioRiskRunResultAdapter.TryAdapt(run, out var results, out var dataAsOf, out var error))
            {
                rejected.Add(new(run.Id, "RESULT_INCOMPLETE", error));
                continue;
            }
            if (dataAsOf is { } asOf && (diagnosis.To.DayNumber - asOf.DayNumber is < 0 or > 7))
            {
                rejected.Add(new(run.Id, "DATA_AS_OF_STALE", "Risk Run data-as-of date is not compatible with the diagnosis end date."));
                continue;
            }
            var reused = results.Select(x => x["operation"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToList();
            var requiredForDiagnosis = new List<string>
            {
                "calculate-annualized-volatility", "calculate-max-drawdown", "calculate-concentration",
                "calculate-historical-var", "calculate-expected-shortfall"
            };
            if (diagnosis.HoldingCount >= 2)
                requiredForDiagnosis.AddRange(["calculate-portfolio-volatility", "calculate-volatility-risk-contribution"]);
            var coreRequired = requiredForDiagnosis.Any(x => !reused.Contains(x, StringComparer.Ordinal));
            var evidence = new PortfolioRiskEvidenceSnapshot(
                [run.Id], PortfolioRiskEvidenceSources.PersistedRiskRun,
                coreRequired ? "PartialHit" : "Hit", dataAsOf, run.InputHash,
                run.AlgorithmVersion, run.DataFactorVersion, reused, [], rejected, coreRequired);
            return (evidence, results);
        }
        return Miss(rejected, "NO_COMPATIBLE_RISK_RUN", "No compatible completed Risk Run was found.");
    }

    internal static string ComputeInputHash(RiskBacktestEngineInput input)
    {
        var json = JsonSerializer.Serialize(input, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static bool TrySnapshot(string json, out RiskInputSnapshot snapshot)
    {
        try
        {
            snapshot = JsonSerializer.Deserialize<RiskInputSnapshot>(json, Json) ?? new(default, default);
            return snapshot.From != default && snapshot.To != default;
        }
        catch (JsonException)
        {
            snapshot = new(default, default);
            return false;
        }
    }

    private static (PortfolioRiskEvidenceSnapshot Evidence, IReadOnlyList<JsonObject> Results) Miss(
        List<PortfolioRiskRunRejection> rejected, string code, string reason)
    {
        if (!rejected.Any(x => x.Code == code)) rejected.Add(new(Guid.Empty, code, reason));
        return (new([], PortfolioRiskEvidenceSources.CacheMiss, "Miss", null, null, null, null,
            [], [], rejected, true), []);
    }

    private sealed record RiskInputSnapshot(DateOnly From, DateOnly To, int Simulations = 10000);
}

internal static class PortfolioRiskRunResultAdapter
{
    public static bool TryAdapt(RiskCalculationRun run, out IReadOnlyList<JsonObject> results,
        out DateOnly? dataAsOfDate, out string error)
    {
        results = [];
        dataAsOfDate = null;
        error = "Risk Run result is empty or invalid.";
        try
        {
            var root = JsonNode.Parse(run.ResultJson!)?.AsObject();
            if (root is null) return false;
            dataAsOfDate = ReadDate(root["dataAsOfDate"]);
            var adapted = new List<JsonObject>();
            Add(adapted, "calculate-annualized-volatility", Decimal(root["historicalAnnualizedVolatility"]), run);
            Add(adapted, "calculate-max-drawdown", Decimal(root["maxDrawdown"]), run);

            var hhi = Decimal(root["concentrationHhi"]);
            var largest = Decimal(root["largestHoldingWeight"]);
            if (hhi is not null)
                Add(adapted, "calculate-concentration", new JsonObject { ["hhi"] = hhi, ["largestWeight"] = largest }, run);

            var oneDay = (root["horizons"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(x => x["horizonDays"]?.GetValue<int>() == 1);
            var historicalVar = Decimal(oneDay?["historicalVaR"]);
            var historicalEs = Decimal(oneDay?["historicalES"]);
            if (oneDay?["confidenceLevels"] is JsonArray levels)
            {
                var level = levels.OfType<JsonObject>().OrderBy(x => Math.Abs((double)((Decimal(x["confidenceLevel"]) ?? 0m) - .95m))).FirstOrDefault();
                historicalVar ??= Decimal(level?["var"]);
                historicalEs ??= Decimal(level?["expectedShortfall"]);
            }
            historicalVar ??= Decimal(root["historical"]?["var95"]);
            historicalEs ??= Decimal(root["historical"]?["es95"]);
            Add(adapted, "calculate-historical-var", historicalVar, run);
            Add(adapted, "calculate-expected-shortfall", historicalEs, run);
            Add(adapted, "calculate-portfolio-volatility", Decimal(root["riskSourceAnnualizedVolatility"]), run);

            if (root["holdings"] is JsonArray holdings && holdings.OfType<JsonObject>().Any())
            {
                var share = holdings.OfType<JsonObject>().Sum(x => Decimal(x["componentRiskShare"]) ?? 0m);
                Add(adapted, "calculate-volatility-risk-contribution", new JsonObject { ["componentRiskShare"] = share }, run);
            }
            if (adapted.Count == 0) return false;
            results = adapted;
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void Add(List<JsonObject> results, string operation, decimal? value, RiskCalculationRun run)
    {
        if (value is null) return;
        Add(results, operation, JsonValue.Create(value), run);
    }

    private static void Add(List<JsonObject> results, string operation, JsonNode? value, RiskCalculationRun run)
    {
        if (value is null) return;
        results.Add(new JsonObject
        {
            ["operation"] = operation,
            ["value"] = value,
            ["source"] = PortfolioRiskEvidenceSources.PersistedRiskRun,
            ["riskRunId"] = run.Id,
            ["algorithmVersion"] = run.AlgorithmVersion,
            ["dataFactorVersion"] = run.DataFactorVersion
        });
    }

    private static decimal? Decimal(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<decimal>(out var parsed)) return parsed;
        return value.TryGetValue<double>(out var floating) ? (decimal)floating : null;
    }

    private static DateOnly? ReadDate(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && DateOnly.TryParse(text, out var date) ? date : null;
}

public sealed class ResolvePortfolioRiskEvidenceNodeHandler(IPortfolioRiskRunResolver resolver) : IAgentNodeHandler
{
    public string NodeType => PortfolioDiagnosisNodeTypes.ResolveRiskEvidence;

    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var diagnosis = PortfolioDiagnosisBlackboard.Context(board);
        var resolved = await resolver.ResolveAsync(diagnosis, context.Run.UserId, cancellationToken);
        var existing = board[AgentBlackboardKeys.MathResults] as JsonArray ?? new JsonArray();
        foreach (var result in resolved.Results) existing.Add(result.DeepClone());
        board[AgentBlackboardKeys.MathResults] = existing;
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.PortfolioRiskEvidence, resolved.Evidence);
        board[AgentBlackboardKeys.CoreRiskCalculationRequired] = resolved.Evidence.CoreCalculationRequired ? "true" : "false";
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Node.OutputJson = AgentNodeJson.Serialize(resolved.Evidence);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated,
            $"Portfolio Risk Run cache: {resolved.Evidence.CacheStatus}.",
            new { resolved.Evidence.CacheStatus, resolved.Evidence.SourceRiskRunIds, resolved.Evidence.ReusedCapabilities, resolved.Evidence.CoreCalculationRequired });
    }
}
