namespace EquityLens.Api.Services.Agents;

public sealed record AgentNodeExecutionPolicy(int TimeoutSeconds, int MaxRetryCount);
public sealed record AgentNodeCatalogEntry(string NodeType, string DisplayName, string Description, string Stage, string SideEffectLevel, IReadOnlyList<string> RequiredBlackboardKeys, IReadOnlyList<string> ProducedBlackboardKeys, IReadOnlyList<string> AllowedNextNodeTypes, AgentNodeExecutionPolicy DefaultPolicy);
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
        N(CriticReviewNodeTypes.LoadResearchRun, "載入研究結果", "載入既有 Research Run。", "Load", "ReadOnly", ["researchRunId"], ["researchRun"], [CriticReviewNodeTypes.BuildEvidencePacket]),
        N(CriticReviewNodeTypes.BuildEvidencePacket, "建立證據封包", "整理供評論使用的證據。", "Evidence", "ReadOnly", ["researchRun"], ["evidencePacket"], [CriticReviewNodeTypes.CheckEvidence]),
        N(CriticReviewNodeTypes.CheckEvidence, "檢查證據", "進行 deterministic evidence coverage 檢查。", "Analyze", "ReadOnly", ["evidencePacket"], ["evidenceCoverageReport"], [CriticReviewNodeTypes.CritiqueAnswer]),
        N(CriticReviewNodeTypes.CritiqueAnswer, "評論答案", "由 Critic Agent 評估答案品質。", "Analyze", "ExternalLlmRead", ["evidencePacket"], ["criticReviewResult"], [CriticReviewNodeTypes.FinalizeCriticReport]),
        N(CriticReviewNodeTypes.FinalizeCriticReport, "完成評論報告", "產生評論結果與路由決策。", "Finalize", "WritesAgentTrace", ["criticReviewResult"], ["criticPolicyDecision"], []),
        N(DraftRevisionNodeTypes.LoadCriticReviewRun, "載入評論結果", "載入已完成的 Critic Review。", "Load", "ReadOnly", ["criticReviewRunId"], ["criticReview"], [DraftRevisionNodeTypes.DraftRevisedAnswer]),
        N(DraftRevisionNodeTypes.DraftRevisedAnswer, "產生修正版", "依評論結果產生修正版答案。", "Act", "ExternalLlmRead", ["criticReview"], ["draftRevisionResult"], [DraftRevisionNodeTypes.FinalizeRevision]),
        N(DraftRevisionNodeTypes.FinalizeRevision, "完成修正版", "輸出最終修正版。", "Finalize", "WritesAgentTrace", ["draftRevisionResult"], ["finalAnswer"], [])
    ];

    public IReadOnlyList<AgentWorkflowCatalogEntry> Workflows { get; } =
    [
        new(AgentWorkflowTypes.CriticReview, "Critic Review", "檢查研究答案與證據品質。", AgentTypes.Critic, [CriticReviewNodeTypes.LoadResearchRun, CriticReviewNodeTypes.BuildEvidencePacket, CriticReviewNodeTypes.CheckEvidence, CriticReviewNodeTypes.CritiqueAnswer, CriticReviewNodeTypes.FinalizeCriticReport], [(CriticReviewNodeTypes.LoadResearchRun, CriticReviewNodeTypes.BuildEvidencePacket), (CriticReviewNodeTypes.BuildEvidencePacket, CriticReviewNodeTypes.CheckEvidence), (CriticReviewNodeTypes.CheckEvidence, CriticReviewNodeTypes.CritiqueAnswer), (CriticReviewNodeTypes.CritiqueAnswer, CriticReviewNodeTypes.FinalizeCriticReport)]),
        new(AgentWorkflowTypes.DraftRevision, "Draft Revision", "根據評論產生修正版研究答案。", AgentTypes.Draft, [DraftRevisionNodeTypes.LoadCriticReviewRun, DraftRevisionNodeTypes.DraftRevisedAnswer, DraftRevisionNodeTypes.FinalizeRevision], [(DraftRevisionNodeTypes.LoadCriticReviewRun, DraftRevisionNodeTypes.DraftRevisedAnswer), (DraftRevisionNodeTypes.DraftRevisedAnswer, DraftRevisionNodeTypes.FinalizeRevision)])
    ];

    public AgentWorkflowCatalogEntry GetWorkflow(string workflowType) => Workflows.Single(x => x.WorkflowType == workflowType);
    public AgentNodeCatalogEntry GetNode(string nodeType) => Nodes.Single(x => x.NodeType == nodeType);
    private static AgentNodeCatalogEntry N(string type, string name, string description, string stage, string effect, IReadOnlyList<string> required, IReadOnlyList<string> produced, IReadOnlyList<string> next) => new(type, name, description, stage, effect, required, produced, next, new AgentNodeExecutionPolicy(120, 0));
}
