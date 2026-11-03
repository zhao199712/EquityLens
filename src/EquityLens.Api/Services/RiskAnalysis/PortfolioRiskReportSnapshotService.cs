using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Portfolios;
using Microsoft.EntityFrameworkCore;

using static EquityLens.Api.Services.RiskAnalysis.RiskModelDefaults;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>建立與讀取正式風險報告快照。</summary>
public sealed class PortfolioRiskReportSnapshotService(
    IPortfolioRepository portfolios,
    IPortfolioRiskCalculator riskService,
    IPortfolioRiskGovernanceService governanceService,
    IPortfolioStressTestService stressService,
    IPortfolioMonteCarloService monteCarloService,
    IPortfolioRiskBacktestService backtestService,
    EquityLensDbContext? dbContext = null) : IPortfolioRiskReportSnapshotService
{
    public async Task<Result<PortfolioRiskReportSnapshotDetailResponse>> CreatePortfolioRiskReportSnapshotAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
    {
        if (dbContext is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.snapshot_unavailable", "Risk report storage is unavailable.");
        var portfolio = await portfolios.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("portfolio.not_found", "Portfolio was not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var risk = await riskService.GetPortfolioRiskAsync(portfolioId, today.AddYears(-1), today, 1, .95m, 10000, providerUserId, cancellationToken, MvewmaFhsModel);
        var governance = await governanceService.GetPortfolioRiskGovernanceAsync(portfolioId, providerUserId, cancellationToken);
        var stress = await stressService.GetPortfolioStressTestAsync(portfolioId, providerUserId, cancellationToken);
        var monteCarlo = await monteCarloService.GetPortfolioMonteCarloAsync(portfolioId, providerUserId, cancellationToken, MvewmaFhsModel);
        var backtest = await backtestService.GetPortfolioRiskBacktestAsync(portfolioId, today.AddYears(-3), today, providerUserId, cancellationToken);
        var unavailable = new List<string>();
        if (!risk.IsSuccess) unavailable.Add("risk: " + risk.ErrorCode);
        if (!governance.IsSuccess) unavailable.Add("governance: " + governance.ErrorCode);
        if (!stress.IsSuccess) unavailable.Add("stress: " + stress.ErrorCode);
        else unavailable.AddRange(stress.Value!.Scenarios.Where(x => x.Status != "ready").Select(x => "stress/" + x.Id + ": " + x.Status));
        if (!monteCarlo.IsSuccess || monteCarlo.Value!.Status != "ready") unavailable.Add("monteCarlo: " + (monteCarlo.ErrorCode ?? monteCarlo.Value?.Status));
        if (!backtest.IsSuccess) unavailable.Add("backtest: " + backtest.ErrorCode);
        else unavailable.AddRange(backtest.Value!.Models.Where(x => x.Status != "ready").Select(x => "backtest/" + x.Model + ": " + x.Status));
        if (unavailable.Count > 0)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.snapshot_unavailable",
                "完整報告缺少可用資料：" + string.Join("；", unavailable) + "。可改為保存已完成計算的快照。");

        var riskValue = risk.Value!;
        var governanceValue = governance.Value!;
        var stressValue = stress.Value!;
        var monteCarloValue = monteCarlo.Value!;
        var backtestValue = backtest.Value!;
        var snapshot = new
        {
            dataQuality = new
            {
                status = governanceValue.DataStatus,
                dataAsOfDate = governanceValue.DataAsOfDate,
                commonTradingDays = governanceValue.CommonTradingDays
            },
            governance = governanceValue.Alerts,
            concentration = new { hhi = riskValue.ConcentrationHhi, largestHoldingWeight = riskValue.LargestHoldingWeight },
            risk = new
            {
                annualizedVolatility = riskValue.HistoricalAnnualizedVolatility,
                maxDrawdown = riskValue.MaxDrawdown,
                sharpeRatio = riskValue.SharpeRatio,
                confidenceLevel = riskValue.ConfidenceLevel,
                measurementBasis = riskValue.MeasurementBasis,
                horizons = riskValue.Horizons.Select(h => new { h.HorizonDays, h.HistoricalVaR, h.HistoricalES, h.EffectiveHistoricalObservations, h.HistoricalStatus, h.MinimumHistoricalObservations, h.MonteCarloVaR, h.MonteCarloES })
            },
            holdings = riskValue.Holdings.Select(h => new { h.Ticker, h.Exchange, h.SecurityName, h.Industry, h.RawIndustry, h.ClassificationVersion, h.Weight, h.AnnualizedVolatility, h.ComponentVolatility, h.ComponentRiskShare, h.MarginalVolatility, h.IncrementalVolatility, h.DataPointCount }),
            industries = riskValue.Industries?.Select(i => new { i.Industry, i.HoldingCount, i.Weight, i.ComponentVolatility, i.ComponentRiskShare, i.MarginalVolatility, i.IncrementalVolatility }),
            backtest = backtestValue.Models.Select(m => new { m.Model, m.ConfidenceLevel, m.ObservationCount, m.BreachCount, m.BreachRate, m.ExpectedBreachRate, m.KupiecPValue, m.ChristoffersenPValue, m.TailObservationCount, m.EsTailLossRatio, m.EsStatus, m.Status }),
            stress = stressValue.Scenarios.Select(s => new
            {
                s.Id,
                s.Name,
                s.Type,
                s.Status,
                s.Methodology,
                s.From,
                s.To,
                s.TotalImpact,
                holdings = s.Holdings.Select(h => new { h.Ticker, h.SecurityName, h.Industry, h.Weight, h.Shock, h.Contribution }),
                industries = s.Industries
            }),
            monteCarlo = new
            {
                monteCarloValue.Status,
                monteCarloValue.DataAsOfDate,
                monteCarloValue.CommonTradingDays,
                monteCarloValue.HorizonDays,
                monteCarloValue.Simulations,
                monteCarloValue.Model,
                monteCarloValue.EwmaLambda,
                monteCarloValue.ShrinkageAlpha,
                monteCarloValue.PositiveReturnProbability,
                monteCarloValue.ExpectedReturn,
                monteCarloValue.P5FinalReturn,
                monteCarloValue.P1FinalReturn,
                monteCarloValue.Diagnostics
            }
        };
        var entity = new RiskReportSnapshot
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            CreatedByUserId = providerUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DataAsOfDate = riskValue.DataAsOfDate,
            Model = "MVEWMA-FHS",
            ThresholdVersion = "balanced-v1",
            SnapshotJson = JsonSerializer.Serialize(snapshot)
        };
        dbContext.RiskReportSnapshots.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<PortfolioRiskReportSnapshotDetailResponse>.Success(ToSnapshotDetail(entity, governanceValue.DataStatus));
    }

    /// <summary>保存指定計算的原始結果與品質狀態，不以不同模型或日期重算。</summary>
    public async Task<Result<PortfolioRiskReportSnapshotDetailResponse>> CreateFromCalculationAsync(
        Guid portfolioId, Guid runId, Guid providerUserId, CancellationToken cancellationToken)
    {
        if (dbContext is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.snapshot_unavailable", "快照儲存不可用。");
        if (await portfolios.GetDetailAsync(portfolioId, providerUserId, cancellationToken) is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        var run = await dbContext.RiskCalculationRuns.AsNoTracking().Include(x => x.QualityEvaluations)
            .SingleOrDefaultAsync(x => x.Id == runId && x.PortfolioId == portfolioId && x.RequestedByUserId == providerUserId, cancellationToken);
        if (run is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.calculation_not_found", "找不到指定的風險計算。");
        if (run.Status != "Completed" || run.Operation != "risk" || string.IsNullOrWhiteSpace(run.ResultJson) || string.IsNullOrWhiteSpace(run.SelectedModel))
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.snapshot_unavailable", "指定的風險計算尚未完成或缺少結果／模型，無法建立快照。");
        using var result = JsonDocument.Parse(run.ResultJson);
        using var input = JsonDocument.Parse(run.InputSnapshotJson);
        var quality = run.QualityEvaluations.OrderByDescending(x => x.EvaluatedAtUtc ?? x.CreatedAtUtc).FirstOrDefault();
        var status = quality?.Status ?? "Unvalidated";
        var asOfDate = result.RootElement.TryGetProperty("dataAsOfDate", out var date) && date.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(date.GetString(), out var parsed) ? parsed : (DateOnly?)null;
        var snapshot = new
        {
            kind = "calculation-run",
            dataQuality = new { status, dataAsOfDate = asOfDate },
            source = new { runId = run.Id, run.InputHash, run.Operation, run.SelectedModel, run.AlgorithmVersion, run.CompletedAtUtc,
                run.FallbackDepth, run.FallbackReason, input = input.RootElement },
            risk = result.RootElement,
            qualityEvaluation = quality is null ? null : new { quality.Status, quality.Model, quality.PolicyVersion, quality.ConfidenceLevel,
                quality.ObservationCount, quality.KupiecPValue, quality.ChristoffersenPValue, quality.EsStatus, quality.FitHealthy,
                failureCodes = JsonSerializer.Deserialize<string[]>(quality.FailureCodesJson),
                warningCodes = JsonSerializer.Deserialize<string[]>(quality.WarningCodesJson), quality.EvaluatedAtUtc },
            scope = "僅保存此 Run 的計算結果及當時品質狀態；未另行執行壓力測試、模擬或其他模型。"
        };
        var entity = new RiskReportSnapshot
        {
            Id = Guid.NewGuid(), PortfolioId = portfolioId, CreatedByUserId = providerUserId, CreatedAtUtc = DateTime.UtcNow,
            DataAsOfDate = asOfDate, Model = run.SelectedModel, ThresholdVersion = quality?.PolicyVersion ?? "unvalidated",
            SnapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        dbContext.RiskReportSnapshots.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<PortfolioRiskReportSnapshotDetailResponse>.Success(ToSnapshotDetail(entity, status));
    }

    public async Task<Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>> GetPortfolioRiskReportSnapshotsAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
    {
        if (dbContext is null)
            return Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>.Failure("risk.snapshot_unavailable", "Risk report storage is unavailable.");
        if (await portfolios.GetDetailAsync(portfolioId, providerUserId, cancellationToken) is null)
            return Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>.Failure("portfolio.not_found", "Portfolio was not found.");
        var reports = await dbContext.RiskReportSnapshots.AsNoTracking()
            .Where(report => report.PortfolioId == portfolioId)
            .OrderByDescending(report => report.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>.Success(reports
            .Select(report => new PortfolioRiskReportSnapshotListItemResponse(report.Id, report.CreatedAtUtc, report.DataAsOfDate, report.Model, report.ThresholdVersion, ReadSnapshotStatus(report.SnapshotJson)))
            .ToList());
    }

    public async Task<Result<PortfolioRiskReportSnapshotDetailResponse>> GetPortfolioRiskReportSnapshotAsync(
        Guid portfolioId, Guid reportId, Guid providerUserId, CancellationToken cancellationToken)
    {
        if (dbContext is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.snapshot_unavailable", "Risk report storage is unavailable.");
        if (await portfolios.GetDetailAsync(portfolioId, providerUserId, cancellationToken) is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        var report = await dbContext.RiskReportSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == reportId && item.PortfolioId == portfolioId, cancellationToken);
        return report is null
            ? Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.report_not_found", "Risk report snapshot was not found.")
            : Result<PortfolioRiskReportSnapshotDetailResponse>.Success(ToSnapshotDetail(report, ReadSnapshotStatus(report.SnapshotJson)));
    }

    private static PortfolioRiskReportSnapshotDetailResponse ToSnapshotDetail(RiskReportSnapshot report, string status)
    {
        using var document = JsonDocument.Parse(report.SnapshotJson);
        return new PortfolioRiskReportSnapshotDetailResponse(report.Id, report.PortfolioId, report.CreatedAtUtc, report.DataAsOfDate, report.Model, report.ThresholdVersion, status, document.RootElement.Clone());
    }

    private static string ReadSnapshotStatus(string snapshotJson)
    {
        using var document = JsonDocument.Parse(snapshotJson);
        return document.RootElement.TryGetProperty("dataQuality", out var quality) && quality.TryGetProperty("status", out var status)
            ? status.GetString() ?? "unknown"
            : "unknown";
    }
}
