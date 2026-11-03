using EquityLens.Api.Services.Agents.Loop;

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
        N(ResearchEntryNodeTypes.Validate, "驗證研究請求", "檢查研究輸入與來源政策。", "Input", "ReadOnly", [AgentBlackboardKeys.ResearchRequest], [AgentBlackboardKeys.Ticker], [ResearchEntryNodeTypes.DetectIntent]),
        N(ResearchEntryNodeTypes.DetectIntent, "辨識研究意圖", "辨識研究問題的意圖。", "Input", "ReadOnly", [AgentBlackboardKeys.Question], [AgentBlackboardKeys.ResearchIntent], [ResearchAgentNodes.Loop]),
        N(ResearchAgentNodes.Loop, "研究迴圈", "有界工具呼叫與可恢復的決策。", "Research", "ExternalLlmRead", [AgentBlackboardKeys.ResearchRequest], [AgentBlackboardKeys.Answer, AgentBlackboardKeys.ResearchRun, AgentBlackboardKeys.Citations, AgentBlackboardKeys.Candidates, AgentBlackboardKeys.SelectedEvidence], [QualityGateNodeHandler.Type], supportsLoop: true),
        N(QualityGateNodeHandler.Type, "品質檢查", "來源、證據、評論與接受／修訂判定。", "Validation", "ExternalLlmRead", [AgentBlackboardKeys.Answer, AgentBlackboardKeys.ResearchRun], ["outputGate", "qualityRoute", AgentBlackboardKeys.EvidencePacket, AgentBlackboardKeys.EvidenceChecks, AgentBlackboardKeys.CriticReview, AgentBlackboardKeys.CriticFindings, AgentBlackboardKeys.FinalOutput], [ResearchAgentNodes.Loop, ResearchAgentNodes.Finalize, CriticReviewNodeTypes.FinalizeCriticReport]),
        N(ResearchAgentNodes.Finalize, "提交研究結果", "品質未通過時發布可辨識的拒答。", "Finalization", "WritesAgentTrace", ["outputGate", AgentBlackboardKeys.CriticReview], [AgentBlackboardKeys.FinalOutput], []),
        N(CriticReviewNodeTypes.LoadResearchRun, "載入研究結果", "讀取授權研究結果。", "Input", "ReadOnly", [AgentBlackboardKeys.ResearchRunId], [AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question, AgentBlackboardKeys.ResearchRun, AgentBlackboardKeys.Answer, AgentBlackboardKeys.Citations, AgentBlackboardKeys.Steps, AgentBlackboardKeys.Candidates], [QualityGateNodeHandler.Type]),
        N(CriticReviewNodeTypes.FinalizeCriticReport, "完成評論報告", "輸出評分，不修改來源答案。", "Finalization", "WritesAgentTrace", [AgentBlackboardKeys.CriticReview], [AgentBlackboardKeys.FinalOutput], []),
        N(PortfolioDiagnosisNodeTypes.LoadContext, "載入投組內容", "驗證投組權限與期間。", "Input", "ReadOnly", [AgentBlackboardKeys.PortfolioId], [AgentBlackboardKeys.PortfolioContext], ["PortfolioAgentLoop"]),
        N("PortfolioAgentLoop", "投組迴圈", "以唯讀工具分析授權投組。", "Analyze", "ExternalLlmRead", [AgentBlackboardKeys.PortfolioContext], ["portfolioLoopAnswer", "agentLoopFacts"], ["PortfolioQualityGate"], supportsLoop: true),
        N("PortfolioQualityGate", "投組品質檢查", "驗證數字與情境證據。", "Validation", "ReadOnly", [AgentBlackboardKeys.PortfolioContext, "portfolioLoopAnswer"], [AgentBlackboardKeys.PortfolioDiagnosisDraft], [HumanApprovalNodeTypes.WaitForHumanApproval]),
        N(HumanApprovalNodeTypes.WaitForHumanApproval, "等待人工審批", "持久化暫停，釋放 worker。", "Approval", "ExternalHumanInteraction", [], [AgentBlackboardKeys.HumanApproval], [PortfolioDiagnosisNodeTypes.FinalizeDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeRejectedDiagnosis]),
        N(PortfolioDiagnosisNodeTypes.FinalizeDiagnosis, "提交投組診斷", "提交已核准診斷。", "Finalization", "WritesAgentTrace", [AgentBlackboardKeys.PortfolioDiagnosisDraft], [AgentBlackboardKeys.FinalOutput], []),
        N(PortfolioDiagnosisNodeTypes.FinalizeRejectedDiagnosis, "記錄拒絕診斷", "保留拒絕理由並結束。", "Finalization", "WritesAgentTrace", [AgentBlackboardKeys.HumanApproval], [AgentBlackboardKeys.FinalOutput], [])
    ];
    public IReadOnlyList<AgentWorkflowCatalogEntry> Workflows { get; } =
    [
        W(AgentWorkflowTypes.ResearchAgent, "研究 Agent", AgentTypes.Research, new ResearchAgentWorkflowDefinitionProvider()),
        W(AgentWorkflowTypes.PortfolioDiagnosis, "投組診斷", AgentTypes.Portfolio, new PortfolioDiagnosisWorkflowDefinitionProvider()),
        W(AgentWorkflowTypes.CriticReview, "研究評分", AgentTypes.Critic, new CriticReviewWorkflowDefinitionProvider())
    ];
    private static AgentWorkflowCatalogEntry W(string type, string name, string agent, IAgentWorkflowDefinitionProvider provider)
    {
        var run = provider.CreateRun(Guid.Empty, Guid.Empty);
        var graph = System.Text.Json.Nodes.JsonNode.Parse(run.WorkflowDefinitionJson)!;
        var nodes = run.Nodes.ToDictionary(n => n.NodeKey, n => n.NodeType);
        return new(type, name, "固定工作流；工具選擇在節點內受控執行。", agent, nodes.Values.Distinct().ToArray(),
            graph["edges"]!.AsArray().Select(e => (nodes[e!["from"]!.GetValue<string>()], nodes[e["to"]!.GetValue<string>()])).Distinct().ToArray());
    }
    public AgentWorkflowCatalogEntry GetWorkflow(string workflowType) => Workflows.Single(x => x.WorkflowType == workflowType);
    public AgentNodeCatalogEntry GetNode(string nodeType) => Nodes.Single(x => x.NodeType == nodeType);
    private static AgentNodeCatalogEntry N(string type, string name, string description, string stage, string effect, IReadOnlyList<string> required, IReadOnlyList<string> produced, IReadOnlyList<string> next, bool supportsLoop = false, bool requiresHumanInput = false) => new(new AgentNodeContract(
        type, 1, name, description, stage, effect,
        $"{type}Input", $"{type}Output", required, [], produced, [], next,
        new AgentNodeExecutionPolicy(120, 0), true, supportsLoop, requiresHumanInput));
}
