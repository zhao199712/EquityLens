namespace EquityLens.Api.Services.Agents;

public sealed record AgentNodeExecutionPolicy(int TimeoutSeconds, int MaxRetryCount);

/// <summary>Immutable design-time contract. This is the single source of truth for planner, validator and Admin.</summary>
public sealed record AgentNodeContract(
    string NodeType,
    int Version,
    string DisplayName,
    string Description,
    string Stage,
    string SideEffectLevel,
    string InputSchema,
    string OutputSchema,
    IReadOnlyList<string> RequiredBlackboardKeys,
    IReadOnlyList<string> OptionalBlackboardKeys,
    IReadOnlyList<string> ProducedBlackboardKeys,
    IReadOnlyList<string> AllowedPreviousNodeTypes,
    IReadOnlyList<string> AllowedNextNodeTypes,
    AgentNodeExecutionPolicy DefaultPolicy,
    bool IsIdempotent,
    bool SupportsLoop,
    bool RequiresHumanInput);

public sealed record AgentNodeCatalogEntry(AgentNodeContract Contract)
{
    public string NodeType => Contract.NodeType;
    public string DisplayName => Contract.DisplayName;
    public string Description => Contract.Description;
    public string Stage => Contract.Stage;
    public string SideEffectLevel => Contract.SideEffectLevel;
    public IReadOnlyList<string> RequiredBlackboardKeys => Contract.RequiredBlackboardKeys;
    public IReadOnlyList<string> ProducedBlackboardKeys => Contract.ProducedBlackboardKeys;
    public IReadOnlyList<string> AllowedNextNodeTypes => Contract.AllowedNextNodeTypes;
    public AgentNodeExecutionPolicy DefaultPolicy => Contract.DefaultPolicy;
}
public sealed record AgentWorkflowCatalogEntry(string WorkflowType, string DisplayName, string Description, string AgentType, IReadOnlyList<string> NodeTypes, IReadOnlyList<(string From, string To)> Edges);

public interface IAgentWorkflowCatalog
{
    IReadOnlyList<AgentWorkflowCatalogEntry> Workflows { get; }
    IReadOnlyList<AgentNodeCatalogEntry> Nodes { get; }
    AgentWorkflowCatalogEntry GetWorkflow(string workflowType);
    AgentNodeCatalogEntry GetNode(string nodeType);
}

public sealed class AgentWorkflowCatalog : IAgentWorkflowCatalog
{
    public IReadOnlyList<AgentNodeCatalogEntry> Nodes { get; } =
    [
        N(CriticReviewNodeTypes.LoadResearchRun, "載入研究結果", "載入既有 Research Run。", "Load", "ReadOnly", [AgentBlackboardKeys.ResearchRunId], [AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question, AgentBlackboardKeys.ResearchRun, AgentBlackboardKeys.Answer, AgentBlackboardKeys.Citations, AgentBlackboardKeys.Steps, AgentBlackboardKeys.Candidates], [CriticReviewNodeTypes.BuildEvidencePacket]),
        N(CriticReviewNodeTypes.BuildEvidencePacket, "建立證據封包", "整理供評論使用的證據。", "Evidence", "ReadOnly", [AgentBlackboardKeys.ResearchRun, AgentBlackboardKeys.Answer], [AgentBlackboardKeys.EvidencePacket], [CriticReviewNodeTypes.CheckEvidence]),
        N(CriticReviewNodeTypes.CheckEvidence, "檢查證據", "進行 deterministic evidence coverage 檢查。", "Analyze", "ReadOnly", [AgentBlackboardKeys.EvidencePacket], [AgentBlackboardKeys.EvidenceChecks, AgentBlackboardKeys.CriticFindings], [CriticReviewNodeTypes.CritiqueAnswer]),
        N(CriticReviewNodeTypes.CritiqueAnswer, "評論答案", "由 Critic Agent 評估答案品質。", "Analyze", "ExternalLlmRead", [AgentBlackboardKeys.EvidencePacket, AgentBlackboardKeys.EvidenceChecks, AgentBlackboardKeys.Answer], [AgentBlackboardKeys.CriticReview, AgentBlackboardKeys.CriticFindings], [CriticReviewNodeTypes.FinalizeCriticReport]),
        N(CriticReviewNodeTypes.FinalizeCriticReport, "完成評論報告", "產生評論結果與路由決策。", "Finalize", "WritesAgentTrace", [AgentBlackboardKeys.CriticReview], [AgentBlackboardKeys.FinalOutput], [DraftRevisionNodeTypes.DraftRevisedAnswer]),
        N(DraftRevisionNodeTypes.LoadCriticReviewRun, "載入評論結果", "載入已完成的 Critic Review。", "Load", "ReadOnly", [AgentBlackboardKeys.CriticReviewRunId], [AgentBlackboardKeys.CriticReviewRun, AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question, AgentBlackboardKeys.Answer, AgentBlackboardKeys.CriticReview, AgentBlackboardKeys.CriticFindings], [DraftRevisionNodeTypes.DraftRevisedAnswer]),
        N(DraftRevisionNodeTypes.DraftRevisedAnswer, "產生修正版", "依評論結果產生修正版答案。", "Act", "ExternalLlmRead", [AgentBlackboardKeys.CriticReview, AgentBlackboardKeys.CriticFindings, AgentBlackboardKeys.Answer], [AgentBlackboardKeys.RevisedAnswer, AgentBlackboardKeys.RevisionSummary, AgentBlackboardKeys.AppliedRecommendation], [DraftRevisionNodeTypes.FinalizeRevision]),
        N(DraftRevisionNodeTypes.FinalizeRevision, "完成修正版", "輸出最終修正版。", "Finalize", "WritesAgentTrace", [AgentBlackboardKeys.CriticReview, AgentBlackboardKeys.Answer, AgentBlackboardKeys.RevisedAnswer, AgentBlackboardKeys.RevisionSummary], [AgentBlackboardKeys.FinalOutput], [])
        ,N(PortfolioDiagnosisNodeTypes.LoadContext, "載入投組診斷內容", "驗證投組與診斷期間。", "Load", "ReadOnly", ["portfolioId"], ["portfolioContext"], [PortfolioDiagnosisNodeTypes.CalculateAttribution])
        ,N(PortfolioDiagnosisNodeTypes.CalculateAttribution, "計算績效歸因", "計算投組相對基準及標的／產業貢獻。", "Analyze", "ReadOnly", ["portfolioContext"], ["performanceAttribution"], [PortfolioDiagnosisNodeTypes.LoadRiskProfile])
        ,N(PortfolioDiagnosisNodeTypes.LoadRiskProfile, "載入風險概況", "載入既有風險治理與資料品質結果。", "Analyze", "ReadOnly", ["portfolioContext"], ["riskProfile"], [PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses])
        ,N(PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses, "排序風險分析", "依資料品質及治理警示排序應補做分析。", "Decide", "ReadOnly", ["performanceAttribution", "riskProfile"], ["riskAnalysisPriorities"], [PortfolioDiagnosisNodeTypes.BuildEvidencePacket])
        ,N(PortfolioDiagnosisNodeTypes.BuildEvidencePacket, "建立投組證據封包", "建立可追溯的診斷證據快照。", "Evidence", "ReadOnly", ["performanceAttribution", "riskProfile", "riskAnalysisPriorities"], ["portfolioEvidencePacket"], [PortfolioDiagnosisNodeTypes.DraftDiagnosis])
        ,N(PortfolioDiagnosisNodeTypes.DraftDiagnosis, "撰寫投組診斷", "以已驗證的證據生成繁體中文診斷。", "Draft", "ExternalLlmRead", ["portfolioEvidencePacket"], ["portfolioDiagnosisDraft"], [PortfolioDiagnosisNodeTypes.FinalizeDiagnosis])
        ,N(PortfolioDiagnosisNodeTypes.FinalizeDiagnosis, "完成投組診斷", "驗證並輸出最終投組診斷。", "Finalize", "WritesAgentTrace", ["portfolioDiagnosisDraft"], ["finalOutput"], [])
        ,N(EvidenceRemediationNodeTypes.LoadContext, "載入補證據內容", "載入 Critic Review 的持久化研究快照與證據缺口。", "Input", "ReadOnly", [AgentBlackboardKeys.CriticReviewRunId], [AgentBlackboardKeys.CriticReviewRun, AgentBlackboardKeys.ResearchRunId, AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question, AgentBlackboardKeys.Answer, AgentBlackboardKeys.Citations, AgentBlackboardKeys.Candidates, AgentBlackboardKeys.CriticFindings, AgentBlackboardKeys.CriticReview], [EvidenceRemediationNodeTypes.PlanRetrieval])
        ,N(EvidenceRemediationNodeTypes.PlanRetrieval, "規劃補充檢索", "依研究問題與 Critic findings 建立有界檢索計畫。", "Planning", "ReadOnly", [AgentBlackboardKeys.Question, AgentBlackboardKeys.CriticFindings], [AgentBlackboardKeys.RetrievalPlan], [EvidenceRemediationNodeTypes.RetrieveEvidence])
        ,N(EvidenceRemediationNodeTypes.RetrieveEvidence, "檢索補充證據", "優先檢索本地文件，覆蓋不足時執行一次 Web fallback。", "Retrieval", "ExternalRead", [AgentBlackboardKeys.Ticker, AgentBlackboardKeys.RetrievalPlan], [AgentBlackboardKeys.RetrievedEvidence], [EvidenceRemediationNodeTypes.ExtractClaims])
        ,N(EvidenceRemediationNodeTypes.ExtractClaims, "擷取待驗證 Claims", "由原始答案擷取需要證據支持的結構化 claims。", "Analysis", "ExternalLlmRead", [AgentBlackboardKeys.Answer], [AgentBlackboardKeys.ExtractedClaims], [EvidenceRemediationNodeTypes.AssessSupport])
        ,N(EvidenceRemediationNodeTypes.AssessSupport, "Evidence Assessor", "評估補充證據對各 claim 的語義支持程度與分析影響。", "Analysis", "ExternalLlmRead", [AgentBlackboardKeys.Question, AgentBlackboardKeys.Answer, AgentBlackboardKeys.CriticFindings, AgentBlackboardKeys.ExtractedClaims, AgentBlackboardKeys.RetrievedEvidence], [AgentBlackboardKeys.ClaimSupportAssessments], [EvidenceRemediationNodeTypes.ValidateMappings])
        ,N(EvidenceRemediationNodeTypes.ValidateMappings, "驗證證據映射", "Deterministic 驗證 citation、數值映射與重新分析訊號。", "Validation", "ReadOnly", [AgentBlackboardKeys.ExtractedClaims, AgentBlackboardKeys.RetrievedEvidence, AgentBlackboardKeys.ClaimSupportAssessments], [AgentBlackboardKeys.EvidenceValidationResults], [EvidenceRemediationNodeTypes.BuildPacket])
        ,N(EvidenceRemediationNodeTypes.Route, "決定補證據路由", "依驗證結果決定完成或進入下一輪，並保存重新分析建議。", "Routing", "ReadOnly", [AgentBlackboardKeys.EvidenceValidationResults], [AgentBlackboardKeys.RouteDecision, AgentBlackboardKeys.UnresolvedClaims, AgentBlackboardKeys.RequiresReanalysis, AgentBlackboardKeys.ReanalysisReasons], [EvidenceRemediationNodeTypes.PlanRetrieval, EvidenceRemediationNodeTypes.BuildPacket])
        ,N(EvidenceRemediationNodeTypes.BuildPacket, "建立補證據封包", "建立可追蹤的 claim-citation 證據封包。", "Evidence", "ReadOnly", [AgentBlackboardKeys.EvidenceValidationResults, AgentBlackboardKeys.RetrievedEvidence], [AgentBlackboardKeys.RemediatedEvidencePacket], [EvidenceRemediationNodeTypes.DraftRevision])
        ,N(EvidenceRemediationNodeTypes.DraftRevision, "產生證據修正版", "只使用已驗證的證據生成修正版。", "Generation", "ExternalLlmRead", [AgentBlackboardKeys.Answer, AgentBlackboardKeys.RemediatedEvidencePacket], [AgentBlackboardKeys.RevisedAnswer, AgentBlackboardKeys.RevisionSummary], [EvidenceRemediationNodeTypes.Finalize])
        ,N(EvidenceRemediationNodeTypes.Finalize, "完成補證據流程", "輸出修正版、補充證據狀態與未解決 claims。", "Finalization", "WritesAgentTrace", [AgentBlackboardKeys.Answer, AgentBlackboardKeys.RevisedAnswer, AgentBlackboardKeys.RevisionSummary, AgentBlackboardKeys.EvidenceValidationResults], [AgentBlackboardKeys.FinalOutput], [])
        ,N(EvidenceReanalysisNodeTypes.Load, "載入補證據結果", "載入需要重新分析的 EvidenceRemediation 持久化結果。", "Input", "ReadOnly", [AgentBlackboardKeys.EvidenceRemediationRunId], [AgentBlackboardKeys.EvidenceRemediationOutput, AgentBlackboardKeys.RemediatedEvidencePacket, AgentBlackboardKeys.ReanalysisReasons], [EvidenceReanalysisNodeTypes.Validate])
        ,N(EvidenceReanalysisNodeTypes.Validate, "驗證重新分析請求", "驗證重新分析理由、claims 與 evidence mappings。", "Validation", "ReadOnly", [AgentBlackboardKeys.EvidenceRemediationOutput, AgentBlackboardKeys.RemediatedEvidencePacket, AgentBlackboardKeys.ReanalysisReasons], [], [EvidenceReanalysisNodeTypes.BuildContext])
        ,N(EvidenceReanalysisNodeTypes.BuildContext, "建立分析內容", "只以已驗證 claims 與證據建立重新分析內容。", "Evidence", "ReadOnly", [AgentBlackboardKeys.RemediatedEvidencePacket, AgentBlackboardKeys.ReanalysisReasons], [AgentBlackboardKeys.AnalysisContext], [EvidenceReanalysisNodeTypes.Reanalyze])
        ,N(EvidenceReanalysisNodeTypes.Reanalyze, "重新執行投資分析", "依已驗證新證據重新推導受影響結論。", "Analysis", "ExternalLlmRead", [AgentBlackboardKeys.AnalysisContext], [AgentBlackboardKeys.ReanalysisDraft], [EvidenceReanalysisNodeTypes.Critique])
        ,N(EvidenceReanalysisNodeTypes.Critique, "審查重新分析", "由獨立 Critic Agent 檢查證據使用與過度推論。", "Critic", "ExternalLlmRead", [AgentBlackboardKeys.AnalysisContext, AgentBlackboardKeys.ReanalysisDraft], [AgentBlackboardKeys.ReanalysisCriticReview, AgentBlackboardKeys.ReanalysisPolicyDecision], [EvidenceReanalysisNodeTypes.Revise])
        ,N(EvidenceReanalysisNodeTypes.Revise, "修訂重新分析", "依 Critic 結果固定執行一次受控修訂。", "Generation", "ExternalLlmRead", [AgentBlackboardKeys.ReanalysisDraft, AgentBlackboardKeys.ReanalysisCriticReview, AgentBlackboardKeys.ReanalysisPolicyDecision], [AgentBlackboardKeys.ReanalysisFinalRevision], [EvidenceReanalysisNodeTypes.Finalize])
        ,N(EvidenceReanalysisNodeTypes.Finalize, "完成重新分析", "輸出重新分析、最終答案、Critic 結果與下一步建議。", "Finalization", "WritesAgentTrace", [AgentBlackboardKeys.AnalysisContext, AgentBlackboardKeys.ReanalysisDraft, AgentBlackboardKeys.ReanalysisFinalRevision], [AgentBlackboardKeys.FinalOutput], [])
    ];

    public IReadOnlyList<AgentWorkflowCatalogEntry> Workflows { get; } =
    [
        new(AgentWorkflowTypes.CriticReview, "Critic Review", "檢查研究答案與證據品質。", AgentTypes.Critic, [CriticReviewNodeTypes.LoadResearchRun, CriticReviewNodeTypes.BuildEvidencePacket, CriticReviewNodeTypes.CheckEvidence, CriticReviewNodeTypes.CritiqueAnswer, CriticReviewNodeTypes.FinalizeCriticReport], [(CriticReviewNodeTypes.LoadResearchRun, CriticReviewNodeTypes.BuildEvidencePacket), (CriticReviewNodeTypes.BuildEvidencePacket, CriticReviewNodeTypes.CheckEvidence), (CriticReviewNodeTypes.CheckEvidence, CriticReviewNodeTypes.CritiqueAnswer), (CriticReviewNodeTypes.CritiqueAnswer, CriticReviewNodeTypes.FinalizeCriticReport)]),
        new(AgentWorkflowTypes.DraftRevision, "Draft Revision", "根據評論產生修正版研究答案。", AgentTypes.Draft, [DraftRevisionNodeTypes.LoadCriticReviewRun, DraftRevisionNodeTypes.DraftRevisedAnswer, DraftRevisionNodeTypes.FinalizeRevision], [(DraftRevisionNodeTypes.LoadCriticReviewRun, DraftRevisionNodeTypes.DraftRevisedAnswer), (DraftRevisionNodeTypes.DraftRevisedAnswer, DraftRevisionNodeTypes.FinalizeRevision)]),
        new(AgentWorkflowTypes.ResearchQualityReview, "研究品質審查", "檢查研究答案與證據品質後產生修正版。", AgentTypes.Critic, [ResearchQualityReviewNodeTypes.LoadResearchRun, ResearchQualityReviewNodeTypes.BuildEvidencePacket, ResearchQualityReviewNodeTypes.CheckEvidence, ResearchQualityReviewNodeTypes.CritiqueAnswer, ResearchQualityReviewNodeTypes.FinalizeCriticReport, ResearchQualityReviewNodeTypes.DraftRevisedAnswer, ResearchQualityReviewNodeTypes.FinalizeRevision], [(ResearchQualityReviewNodeTypes.LoadResearchRun, ResearchQualityReviewNodeTypes.BuildEvidencePacket), (ResearchQualityReviewNodeTypes.BuildEvidencePacket, ResearchQualityReviewNodeTypes.CheckEvidence), (ResearchQualityReviewNodeTypes.CheckEvidence, ResearchQualityReviewNodeTypes.CritiqueAnswer), (ResearchQualityReviewNodeTypes.CritiqueAnswer, ResearchQualityReviewNodeTypes.FinalizeCriticReport), (ResearchQualityReviewNodeTypes.FinalizeCriticReport, ResearchQualityReviewNodeTypes.DraftRevisedAnswer), (ResearchQualityReviewNodeTypes.DraftRevisedAnswer, ResearchQualityReviewNodeTypes.FinalizeRevision)])
        ,new(AgentWorkflowTypes.PortfolioDiagnosis, "Portfolio Diagnosis", "分析投組跑輸來源與應補做風險分析。", AgentTypes.Portfolio, [PortfolioDiagnosisNodeTypes.LoadContext, PortfolioDiagnosisNodeTypes.CalculateAttribution, PortfolioDiagnosisNodeTypes.LoadRiskProfile, PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeTypes.BuildEvidencePacket, PortfolioDiagnosisNodeTypes.DraftDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeDiagnosis], [(PortfolioDiagnosisNodeTypes.LoadContext, PortfolioDiagnosisNodeTypes.CalculateAttribution), (PortfolioDiagnosisNodeTypes.CalculateAttribution, PortfolioDiagnosisNodeTypes.LoadRiskProfile), (PortfolioDiagnosisNodeTypes.LoadRiskProfile, PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses), (PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeTypes.BuildEvidencePacket), (PortfolioDiagnosisNodeTypes.BuildEvidencePacket, PortfolioDiagnosisNodeTypes.DraftDiagnosis), (PortfolioDiagnosisNodeTypes.DraftDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeDiagnosis)])
        ,new(AgentWorkflowTypes.EvidenceRemediation, "證據補強與修訂", "依 Critic findings 補充證據並產生可驗證修正版。", AgentTypes.Research, EvidenceRemediationTypes(), EvidenceRemediationEdges())
        ,new(AgentWorkflowTypes.EvidenceReanalysis, "證據驅動重新分析", "依已驗證補充證據重新分析、獨立審查並完成一次修訂。", AgentTypes.Analysis, EvidenceReanalysisTypes(), EvidenceReanalysisEdges())
    ];

    public AgentWorkflowCatalogEntry GetWorkflow(string workflowType) => Workflows.Single(x => x.WorkflowType == workflowType);
    public AgentNodeCatalogEntry GetNode(string nodeType) => Nodes.Single(x => x.NodeType == nodeType);
    private static IReadOnlyList<string> EvidenceRemediationTypes() => [EvidenceRemediationNodeTypes.LoadContext, EvidenceRemediationNodeTypes.ExtractClaims, EvidenceRemediationNodeTypes.PlanRetrieval, EvidenceRemediationNodeTypes.RetrieveEvidence, EvidenceRemediationNodeTypes.AssessSupport, EvidenceRemediationNodeTypes.ValidateMappings, EvidenceRemediationNodeTypes.Route, EvidenceRemediationNodeTypes.BuildPacket, EvidenceRemediationNodeTypes.DraftRevision, EvidenceRemediationNodeTypes.Finalize];
    private static IReadOnlyList<(string From, string To)> EvidenceRemediationEdges() => EvidenceRemediationTypes().Zip(EvidenceRemediationTypes().Skip(1), (from, to) => (from, to)).ToList();
    private static IReadOnlyList<string> EvidenceReanalysisTypes() => [EvidenceReanalysisNodeTypes.Load, EvidenceReanalysisNodeTypes.Validate, EvidenceReanalysisNodeTypes.BuildContext, EvidenceReanalysisNodeTypes.Reanalyze, EvidenceReanalysisNodeTypes.Critique, EvidenceReanalysisNodeTypes.Revise, EvidenceReanalysisNodeTypes.Finalize];
    private static IReadOnlyList<(string From, string To)> EvidenceReanalysisEdges() => EvidenceReanalysisTypes().Zip(EvidenceReanalysisTypes().Skip(1), (from, to) => (from, to)).ToList();
    private static AgentNodeCatalogEntry N(string type, string name, string description, string stage, string effect, IReadOnlyList<string> required, IReadOnlyList<string> produced, IReadOnlyList<string> next) => new(new AgentNodeContract(
        type, 1, name, description, stage, effect,
        $"{type}Input", $"{type}Output", required, [], produced, [], next,
        new AgentNodeExecutionPolicy(120, 0), true, false, false));
}
