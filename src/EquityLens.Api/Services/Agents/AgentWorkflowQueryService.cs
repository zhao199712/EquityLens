using System.Globalization;
using System.Text.RegularExpressions;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public interface IAgentWorkflowQueryService
{
    Task<AgentWorkflowQueryCreatedResponse> CreateAsync(Guid userId, CreateAgentWorkflowQueryRequest request, CancellationToken cancellationToken = default);
}

public sealed class AgentWorkflowQueryException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class AgentWorkflowQueryService(
    EquityLensDbContext db,
    IInvestmentResearchRouter router,
    IAgentRunService agentRuns) : IAgentWorkflowQueryService
{
    public async Task<AgentWorkflowQueryCreatedResponse> CreateAsync(Guid userId, CreateAgentWorkflowQueryRequest request, CancellationToken cancellationToken = default)
    {
        var question = request.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question)) throw new AgentWorkflowQueryException("question_required", "請輸入問題。");
        if (question.Length > 2000) throw new AgentWorkflowQueryException("question_too_long", "問題不可超過 2000 個字元。");

        var portfolios = await db.Portfolios.AsNoTracking()
            .Where(x => x.OwnerUserId == userId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new InvestmentResearchPortfolioOption(x.Id, x.Name, x.BaseCurrency, x.Holdings.Count))
            .ToListAsync(cancellationToken);
        var decision = await router.RouteAsync(question, portfolios, cancellationToken, userId);

        if (string.Equals(decision.WorkflowType, AgentWorkflowTypes.PortfolioDiagnosis, StringComparison.OrdinalIgnoreCase))
        {
            var portfolioId = ResolvePortfolio(decision.PortfolioId, portfolios);
            var (from, to) = ResolveDiagnosisWindow(decision.RoutingContext.ContextEnvelope.Horizon, question);
            var created = await agentRuns.CreatePortfolioDiagnosisAsync(userId, portfolioId, from, to, decision.RoutingContext, cancellationToken);
            return Response(created.Id, null, created.WorkflowType, created.Status, decision.RoutingContext);
        }

        if (string.Equals(decision.WorkflowType, AgentWorkflowTypes.ResearchInvestigation, StringComparison.OrdinalIgnoreCase))
        {
            var ticker = await ResolveTickerAsync(decision.SecurityQuery, question, cancellationToken);
            var created = await agentRuns.CreateResearchInvestigationAsync(userId, new ResearchAskRequest(ticker, question, SourcePolicy: SourcePolicy.Auto), decision.RoutingContext, cancellationToken);
            return Response(created.AgentRun.Id, created.ResearchRunId, created.AgentRun.WorkflowType, created.AgentRun.Status, decision.RoutingContext);
        }

        throw new AgentWorkflowQueryException("unsupported_workflow", "LLM 未選出受支援的 workflow。");
    }

    private static AgentWorkflowQueryCreatedResponse Response(
        Guid runId,
        Guid? researchRunId,
        string workflowType,
        string status,
        InvestmentResearchRoutingContext routing) =>
        new(runId, researchRunId, workflowType, status, routing.RoutingReason, routing.RoutingModel,
            routing.LeadSkill, routing.LeadSkillDisplayName, routing.Confidence, routing.Objective,
            routing.ContextEnvelope, routing.InferredFields, routing.ClarifyingQuestions);

    private static Guid ResolvePortfolio(Guid? selected, IReadOnlyList<InvestmentResearchPortfolioOption> portfolios)
    {
        if (portfolios.Count == 1) return portfolios[0].Id;
        if (portfolios.Count == 0) throw new AgentWorkflowQueryException("portfolio_required", "目前沒有可供診斷的投資組合。");
        if (selected.HasValue && portfolios.Any(x => x.Id == selected.Value)) return selected.Value;
        throw new AgentWorkflowQueryException("portfolio_required", "問題被判定為 Portfolio Diagnosis；請在問題中寫明要診斷的投資組合名稱。");
    }

    private static (DateOnly? From, DateOnly? To) ResolveDiagnosisWindow(string? horizon, string question)
    {
        var months = ParseHorizonMonths(horizon) ?? ParseQuestionHorizonMonths(question);
        if (months is null) return (null, null);
        var end = DateOnly.FromDateTime(DateTime.UtcNow);
        return (end.AddMonths(-months.Value), end);
    }

    public static int? ParseHorizonMonths(string? horizon)
    {
        if (string.IsNullOrWhiteSpace(horizon)) return null;
        var match = Regex.Match(horizon.Trim(), @"^(\d+(?:\.\d+)?)\s*(y|year|years|m|month|months|d|day|days|年|月|天|日)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var amount = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var months = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "y" or "year" or "years" or "年" => (int)(amount * 12m),
            "m" or "month" or "months" or "月" => (int)amount,
            _ => (int)(amount / 30m)
        };
        return Math.Clamp(months, 1, 120);
    }

    private static readonly Regex QuestionHorizonRegex = new(
        @"(?:最近|近|過去|以來|這|此)\s*(?:(\d+(?:\.\d+)?)|([一二三四五六七八九十兩]))\s*(年|個月|季|天|日)",
        RegexOptions.CultureInvariant);

    public static int? ParseQuestionHorizonMonths(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return null;
        if (question.Contains("半年", StringComparison.Ordinal)) return 6;
        var match = QuestionHorizonRegex.Match(question);
        if (!match.Success) return null;
        var amount = match.Groups[1].Success
            ? decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : ParseChineseNumber(match.Groups[2].Value) ?? 0m;
        if (amount <= 0) return null;
        var months = match.Groups[3].Value switch
        {
            "年" => (int)(amount * 12m),
            "個月" => (int)amount,
            "季" => (int)(amount * 3m),
            _ => (int)(amount / 30m)
        };
        return Math.Clamp(months, 1, 120);
    }

    private static decimal? ParseChineseNumber(string token) => token switch
    {
        "一" => 1, "二" => 2, "兩" => 2, "三" => 3, "四" => 4, "五" => 5,
        "六" => 6, "七" => 7, "八" => 8, "九" => 9, "十" => 10,
        _ => null
    };

    private async Task<string> ResolveTickerAsync(string? securityQuery, string originalQuestion, CancellationToken cancellationToken)
    {
        var lookup = securityQuery?.Trim();
        if (lookup?.Length > 100) lookup = null;

        var securities = await db.Securities.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new SecurityOption(x.Ticker, x.Name))
            .ToListAsync(cancellationToken);

        var llmMatches = string.IsNullOrWhiteSpace(lookup)
            ? []
            : FindByLookup(securities, lookup);
        if (llmMatches.Count == 1) return NormalizeTicker(llmMatches[0].Ticker);

        // The LLM may rewrite a name despite the contract. Resolve the original,
        // user-controlled text deterministically before returning an error.
        var explicitTickerMatches = securities
            .Where(x => ContainsTicker(originalQuestion, x.Ticker))
            .ToList();
        if (explicitTickerMatches.Count == 1) return NormalizeTicker(explicitTickerMatches[0].Ticker);
        if (explicitTickerMatches.Count > 1)
            throw new AgentWorkflowQueryException("security_ambiguous", "問題中包含多個股票代號，請一次指定一家公司。");

        var originalNameMatches = securities
            .Where(x => !string.IsNullOrWhiteSpace(x.Name)
                && originalQuestion.Contains(x.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (originalNameMatches.Count == 1) return NormalizeTicker(originalNameMatches[0].Ticker);
        if (originalNameMatches.Count > 1)
            throw new AgentWorkflowQueryException("security_ambiguous", "問題中包含多家公司名稱，請一次指定一家公司。");

        if (llmMatches.Count > 1)
            throw new AgentWorkflowQueryException("security_ambiguous", $"「{lookup}」對應多個證券，請在問題中提供股票代號。");
        if (string.IsNullOrWhiteSpace(lookup))
            throw new AgentWorkflowQueryException("security_required", "問題被判定為 Research Investigation；請在問題中寫明公司名稱或股票代號。");
        throw new AgentWorkflowQueryException("security_not_found", $"找不到「{lookup}」對應的證券，請改用股票代號或完整公司名稱。");
    }

    private static List<SecurityOption> FindByLookup(IReadOnlyList<SecurityOption> securities, string lookup)
    {
        var exact = securities.Where(x => string.Equals(x.Ticker, lookup, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Name, lookup, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count > 0
            ? exact
            : securities.Where(x => x.Name.Contains(lookup, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static bool ContainsTicker(string question, string ticker) =>
        !string.IsNullOrWhiteSpace(ticker)
        && Regex.IsMatch(question, $@"(?<![A-Za-z0-9]){Regex.Escape(ticker.Trim())}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string NormalizeTicker(string ticker) => ticker.Trim().ToUpperInvariant();

    private sealed record SecurityOption(string Ticker, string Name);
}
