namespace EquityLens.Api.Services.Agents;

public sealed record NodeMetadata(
    string NodeType,
    string Stage,
    IReadOnlyList<string> RequiredBlackboardKeys,
    IReadOnlyList<string> ProducedBlackboardKeys);

public static class AgentNodeMetadata
{
    public static NodeMetadata LoadResearchRun { get; } = new(
        CriticReviewNodeTypes.LoadResearchRun,
        "Load",
        [AgentBlackboardKeys.ResearchRunId],
        [AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question, AgentBlackboardKeys.ResearchRun,
         AgentBlackboardKeys.Answer, AgentBlackboardKeys.Citations, AgentBlackboardKeys.Steps, AgentBlackboardKeys.Candidates]);

    public static NodeMetadata BuildEvidencePacket { get; } = new(
        CriticReviewNodeTypes.BuildEvidencePacket,
        "Evidence",
        [AgentBlackboardKeys.ResearchRun, AgentBlackboardKeys.Answer],
        [AgentBlackboardKeys.EvidencePacket]);

    public static NodeMetadata CheckEvidence { get; } = new(
        CriticReviewNodeTypes.CheckEvidence,
        "Evidence",
        [AgentBlackboardKeys.EvidencePacket],
        [AgentBlackboardKeys.EvidenceChecks, AgentBlackboardKeys.CriticFindings]);

    public static NodeMetadata CritiqueAnswer { get; } = new(
        CriticReviewNodeTypes.CritiqueAnswer,
        "Critic",
        [AgentBlackboardKeys.EvidencePacket, AgentBlackboardKeys.EvidenceChecks, AgentBlackboardKeys.Answer],
        [AgentBlackboardKeys.CriticReview, AgentBlackboardKeys.CriticFindings]);

    public static NodeMetadata FinalizeCriticReport { get; } = new(
        CriticReviewNodeTypes.FinalizeCriticReport,
        "Critic",
        [AgentBlackboardKeys.CriticReview],
        [AgentBlackboardKeys.FinalOutput]);

    public static NodeMetadata LoadCriticReviewRun { get; } = new(
        DraftRevisionNodeTypes.LoadCriticReviewRun,
        "Load",
        [AgentBlackboardKeys.CriticReviewRunId],
        [AgentBlackboardKeys.CriticReviewRun, AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question,
         AgentBlackboardKeys.Answer, AgentBlackboardKeys.CriticReview, AgentBlackboardKeys.CriticFindings]);

    public static NodeMetadata DraftRevisedAnswer { get; } = new(
        DraftRevisionNodeTypes.DraftRevisedAnswer,
        "Draft",
        [AgentBlackboardKeys.CriticReview, AgentBlackboardKeys.CriticFindings, AgentBlackboardKeys.Answer],
        [AgentBlackboardKeys.RevisedAnswer, AgentBlackboardKeys.RevisionSummary, AgentBlackboardKeys.AppliedRecommendation]);

    public static NodeMetadata FinalizeRevision { get; } = new(
        DraftRevisionNodeTypes.FinalizeRevision,
        "Draft",
        [AgentBlackboardKeys.CriticReview, AgentBlackboardKeys.Answer,
         AgentBlackboardKeys.RevisedAnswer, AgentBlackboardKeys.RevisionSummary],
        [AgentBlackboardKeys.FinalOutput]);

    private static readonly IReadOnlyDictionary<string, NodeMetadata> ByNodeType =
        new Dictionary<string, NodeMetadata>(StringComparer.Ordinal)
        {
            [LoadResearchRun.NodeType] = LoadResearchRun,
            [BuildEvidencePacket.NodeType] = BuildEvidencePacket,
            [CheckEvidence.NodeType] = CheckEvidence,
            [CritiqueAnswer.NodeType] = CritiqueAnswer,
            [FinalizeCriticReport.NodeType] = FinalizeCriticReport,
            [LoadCriticReviewRun.NodeType] = LoadCriticReviewRun,
            [DraftRevisedAnswer.NodeType] = DraftRevisedAnswer,
            [FinalizeRevision.NodeType] = FinalizeRevision,
        };

    public static NodeMetadata? GetByNodeType(string nodeType) =>
        ByNodeType.TryGetValue(nodeType, out var metadata) ? metadata : null;

    public static IReadOnlyDictionary<string, NodeMetadata> GetAll() => ByNodeType;
}

public static class NodeMetadataValidator
{
    public static void ValidateBlackboardDependencies(
        IReadOnlyList<string> executionOrder,
        string initialBlackboardJson,
        IReadOnlyDictionary<string, NodeMetadata> metadataRegistry)
    {
        var blackboard = AgentNodeJson.ParseBlackboard(initialBlackboardJson);
        var producedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nodeKey in executionOrder)
        {
            var metadata = metadataRegistry.Values.FirstOrDefault(m =>
                string.Equals(m.NodeType, nodeKey, StringComparison.Ordinal));
            if (metadata is null)
            {
                continue;
            }

            foreach (var requiredKey in metadata.RequiredBlackboardKeys)
            {
                var inInitialBlackboard = blackboard.ContainsKey(requiredKey) && blackboard[requiredKey] is not null;
                var producedByPreviousNode = producedKeys.Contains(requiredKey);
                if (!inInitialBlackboard && !producedByPreviousNode)
                {
                    throw new InvalidOperationException(
                        $"Node '{nodeKey}' requires blackboard key '{requiredKey}' but it is not present. " +
                        $"Ensure a previous node produces this key or it is in the initial blackboard.");
                }
            }

            foreach (var producedKey in metadata.ProducedBlackboardKeys)
            {
                producedKeys.Add(producedKey);
            }
        }
    }
}
