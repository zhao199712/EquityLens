using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public interface IAgentWorkflowQueryService
{
    Task<AgentWorkflowQueryCreatedResponse> CreateAsync(Guid userId, CreateAgentWorkflowQueryRequest request, CancellationToken cancellationToken = default);
    Task<AgentWorkflowQueryCreatedResponse> CreateFromDecisionAsync(Guid userId, CreateAgentWorkflowQueryRequest request, InvestmentResearchRouteDecision decision, CancellationToken cancellationToken = default)
        => CreateAsync(userId, request, cancellationToken);
}

public sealed class AgentWorkflowQueryException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class AgentWorkflowQueryService(
    EquityLensDbContext db,
    IInvestmentResearchRouter router,
    IAgentRunService agentRuns, ILogger<AgentWorkflowQueryService>? logger = null) : IAgentWorkflowQueryService
{
    public Task<AgentWorkflowQueryCreatedResponse> CreateAsync(Guid userId, CreateAgentWorkflowQueryRequest request, CancellationToken cancellationToken = default)
        => CreateCoreAsync(userId, request, null, cancellationToken);
    public Task<AgentWorkflowQueryCreatedResponse> CreateFromDecisionAsync(Guid userId, CreateAgentWorkflowQueryRequest request, InvestmentResearchRouteDecision decision, CancellationToken cancellationToken = default)
        => CreateCoreAsync(userId, request, decision, cancellationToken);
    private async Task<AgentWorkflowQueryCreatedResponse> CreateCoreAsync(Guid userId, CreateAgentWorkflowQueryRequest request, InvestmentResearchRouteDecision? selected, CancellationToken cancellationToken)
    {
        var question = request.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question)) throw new AgentWorkflowQueryException("question_required", "請輸入問題。");
        if (question.Length > 2000) throw new AgentWorkflowQueryException("question_too_long", "問題不可超過 2000 個字元。");

        var portfolios = await db.Portfolios.AsNoTracking()
            .Where(x => x.OwnerUserId == userId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new InvestmentResearchPortfolioOption(x.Id, x.Name, x.BaseCurrency, x.Holdings.Count))
            .ToListAsync(cancellationToken);
        if (ExplicitPortfolioAccess.RequestsUnownedPortfolio(question, portfolios.Select(p => p.Id)))
        {
            logger?.LogWarning("Routing rejected: {Category}; field={Field}", "unauthorized_portfolio", "message.portfolioId");
            throw new AgentWorkflowQueryException("portfolio_forbidden", "無法使用未授權的投資組合。");
        }
        PortfolioDiagnosisInput? parentInput = null;
        string? parentResearchTicker = null;
        string? parentWorkflow = null;
        if (request.ParentAgentRunId is Guid parentId)
        {
            var parent = await db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parentId && x.UserId == userId, cancellationToken);
            if (parent is null || parent.Status != AgentRunStatuses.Succeeded || parent.WorkflowType is not (AgentWorkflowTypes.PortfolioDiagnosis or AgentWorkflowTypes.ResearchAgent))
                throw new AgentWorkflowQueryException("previous_run_not_ready", "原分析尚未完成或無法存取。");
            parentWorkflow = parent.WorkflowType;
            if (parent.WorkflowType == AgentWorkflowTypes.PortfolioDiagnosis)
                parentInput = PortfolioDiagnosisWorkflowDefinitionProvider.ParseInput(parent.InputJson);
            else
            {
                var research = await db.ResearchRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parent.ResearchRunId && x.UserId == userId, cancellationToken)
                    ?? throw new AgentWorkflowQueryException("previous_run_not_ready", "無法存取原研究。");
                parentResearchTicker = research.Ticker;
                question = $"原研究問題：{research.Question}。追問：{question}";
            }
        }
        var securities = await db.Securities.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Ticker, x.Name }).ToListAsync(cancellationToken);
        var candidates = securities.Where(x => !string.IsNullOrWhiteSpace(x.Name) && request.Question!.Contains(x.Name, StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(request.Question!, $@"(?<![A-Za-z0-9]){Regex.Escape(x.Ticker)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase)).Take(10).ToArray();
        if (parentResearchTicker is not null && candidates.Length == 1) parentResearchTicker = candidates[0].Ticker;
        var decision = parentWorkflow is not null
            ? InvestmentResearchRouter.Fixed(parentWorkflow, question, parentInput?.PortfolioId, parentResearchTicker)
            : selected ?? await router.RouteWithCandidatesAsync(question, portfolios, JsonSerializer.SerializeToNode(candidates, AgentNodeJson.SerializerOptions)!.AsArray(), cancellationToken);
        if (decision.RoutingContext.Confidence == "low" || decision.RoutingContext.ClarifyingQuestions.Count > 0)
            throw new AgentWorkflowQueryException("routing_clarification_required", decision.RoutingContext.ClarifyingQuestions.Count > 0 ? string.Join(" ", decision.RoutingContext.ClarifyingQuestions) : "請明確說明研究標的或投組與調整目標。");
        if (decision.PortfolioId is Guid selectedId && !portfolios.Any(p => p.Id == selectedId))
        {
            logger?.LogWarning("Routing rejected: {Category}; field={Field}", "unauthorized_portfolio", "params.portfolioId");
            throw new AgentWorkflowQueryException("portfolio_forbidden", "無法使用未授權的投資組合。");
        }

        if (string.Equals(decision.WorkflowType, AgentWorkflowTypes.PortfolioDiagnosis, StringComparison.OrdinalIgnoreCase))
        {
            if (decision.RoutingContext.ClarifyingQuestions.Count > 0)
                throw new AgentWorkflowQueryException("scenario_clarification_required", string.Join(" ", decision.RoutingContext.ClarifyingQuestions));
            var portfolioId = parentInput?.PortfolioId ?? ResolvePortfolio(decision.PortfolioId, portfolios, question);
            PortfolioScenarioCalculator.ValidateRequests(decision.RoutingContext.Scenarios);
            await ValidateScenarioIntentAsync(portfolioId, question, decision.RoutingContext.Scenarios, cancellationToken);
            var (from, to) = ResolveDiagnosisWindow(decision.RoutingContext.ContextEnvelope.Horizon, question);
            if (parentInput is not null) { from = parentInput.From; to = parentInput.To; }
            var routing = decision.RoutingContext with { ParentAgentRunId = request.ParentAgentRunId };
            var created = await agentRuns.CreatePortfolioDiagnosisAsync(userId, portfolioId, from, to, routing, cancellationToken);
            return Response(created.Id, null, created.WorkflowType, created.Status, decision.RoutingContext);
        }

        if (string.Equals(decision.WorkflowType, AgentWorkflowTypes.ResearchAgent, StringComparison.OrdinalIgnoreCase))
        {
            var ticker = parentResearchTicker ?? await ResolveTickerAsync(decision.SecurityQuery, question, cancellationToken);
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

    private static Guid ResolvePortfolio(Guid? selected, IReadOnlyList<InvestmentResearchPortfolioOption> portfolios, string question)
    {
        if (portfolios.Count == 1) return portfolios[0].Id;
        if (portfolios.Count == 0) throw new AgentWorkflowQueryException("portfolio_required", "目前沒有可供診斷的投資組合。");
        var named = portfolios.Where(p => question.Contains(p.Name, StringComparison.OrdinalIgnoreCase) || question.Contains(p.Id.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (named.Length == 1 && (selected is null || named[0].Id == selected)) return named[0].Id;
        throw new AgentWorkflowQueryException("portfolio_required", "請指定要診斷的投資組合：" + string.Join("、", portfolios.Select(p => p.Name)));
    }

    private async Task ValidateScenarioIntentAsync(Guid portfolioId, string question, IReadOnlyList<PortfolioScenarioRequest>? scenarios, CancellationToken ct)
    {
        if (Regex.IsMatch(question, @"放空|借款|槓桿|融資"))
            throw new AgentWorkflowQueryException("scenario_clarification_required", "情境只支援減碼留現金、轉入既有持股或核准指數 ETF，請確認調整方式。");
        var holdings = await db.PortfolioHoldings.AsNoTracking().Where(h => h.PortfolioId == portfolioId)
            .Select(h => new { h.Security.Ticker, h.Security.Name }).ToListAsync(ct);
        if (scenarios is { Count: > 0 } && scenarios.Any(s => s.TargetWeights.Keys.Any(t => !holdings.Any(h => h.Ticker == t))
            || s.TransferToTicker is not null && !(s.RemainderPolicy == "index_etf" ? PortfolioIndexEtfs.IsSupported(s.TransferToTicker) : holdings.Any(h => h.Ticker == s.TransferToTicker))))
            throw new AgentWorkflowQueryException("scenario_clarification_required", "減碼來源須為既有持股；轉入只支援既有持股或核准指數 ETF。");
        var transfer = Regex.Match(question, @"(?:轉入|轉到|移到|移入|移至|轉成|轉為)\s*([^，。；？\s]+)");
        if (transfer.Success)
        {
            var target = transfer.Groups[1].Value;
            var destination = holdings.Select(h => (h.Ticker, h.Name)).Concat(PortfolioIndexEtfs.Universe.Select(e => (e.Ticker, e.Name)))
                .Where(h => Regex.IsMatch(target, @"(?<![\dA-Za-z])" + Regex.Escape(h.Ticker) + @"(?![\dA-Za-z])", RegexOptions.IgnoreCase)
                    || (!string.IsNullOrWhiteSpace(h.Name) && h.Name != h.Ticker && Regex.IsMatch(target, Regex.Escape(h.Name) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase)))
                .DistinctBy(h => h.Ticker).ToArray();
            var cash = target.Contains("現金");
            if (scenarios is null || scenarios.Count == 0 || (!cash && destination.Length != 1)
                || scenarios.Any(s => cash ? s.RemainderPolicy != "cash" : s.RemainderPolicy is not ("holding" or "index_etf") || s.TransferToTicker != destination[0].Ticker))
                throw new AgentWorkflowQueryException("scenario_clarification_required", "請確認減碼目標權重及轉入對象；不會以其他情境代替。");
        }
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
