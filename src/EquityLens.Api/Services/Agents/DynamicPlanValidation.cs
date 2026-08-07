using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public sealed record ValidatedDynamicPlan(DynamicPlanProposal Proposal, IReadOnlyList<DynamicPlanAction> Actions);
public interface IDynamicPlanValidator { ValidatedDynamicPlan Validate(AgentRun run, DynamicPlanProposal proposal); }

public sealed class DynamicPlanValidator(INodeCapabilityRegistry capabilities, IAgentWorkflowCatalog catalog, IWorkflowGraphTopologyService topology) : IDynamicPlanValidator
{
    public ValidatedDynamicPlan Validate(AgentRun run, DynamicPlanProposal proposal)
    {
        if (proposal.BaseOrchestrationVersion != run.OrchestrationVersion) throw new InvalidOperationException("Dynamic plan is stale.");
        if (proposal.GoalStatus is not (DynamicGoalStatuses.Continue or DynamicGoalStatuses.Complete)) throw new InvalidOperationException("Dynamic plan goalStatus is invalid.");
        if ((proposal.Trigger is DynamicPlanningTriggers.ResearchContextReady or DynamicPlanningTriggers.FeedbackContextReady) && proposal.GoalStatus != DynamicGoalStatuses.Continue)
            throw new InvalidOperationException("Initial planning must continue with an executable branch.");
        var skillCatalog = new WorkflowSkillCatalog();
        var allowedSkills = skillCatalog.Skills.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (proposal.SelectedSkills.Any(x => !allowedSkills.Contains(x))) throw new InvalidOperationException("Dynamic plan selected an unknown skill.");
        if (proposal.GoalStatus == DynamicGoalStatuses.Complete && proposal.Actions.Count > 0) throw new InvalidOperationException("A completed plan cannot contain actions.");
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); var review = board[AgentBlackboardKeys.CriticReview] as JsonObject;
        if ((proposal.Trigger is DynamicPlanningTriggers.ResearchContextReady or DynamicPlanningTriggers.CapabilityRequestsReady)
            && board[AgentBlackboardKeys.LeadSkill]?.GetValue<string>() is { Length: > 0 } leadSkillId)
        {
            var leadSkill = skillCatalog.Skills.SingleOrDefault(x => x.Id == leadSkillId && x.Routable)
                ?? throw new InvalidOperationException($"Routed lead skill '{leadSkillId}' is not registered.");
            if (proposal.SelectedSkills.Count != 1 || proposal.SelectedSkills[0] != leadSkillId)
                throw new InvalidOperationException("Initial plan must select exactly the routed lead skill.");
            var leadCapabilities = leadSkill.Capabilities.ToHashSet(StringComparer.Ordinal);
            var unauthorized = proposal.Actions.FirstOrDefault(x => !leadCapabilities.Contains(x.Capability));
            if (unauthorized is not null)
                throw new InvalidOperationException($"Capability '{unauthorized.Capability}' is not authorized by lead skill '{leadSkillId}'.");
        }
        var finalizationCompleted = run.Nodes.Any(x => x.Status == AgentNodeStatuses.Succeeded && x.NodeType is (DraftRevisionNodeTypes.FinalizeRevision or EvidenceRemediationNodeTypes.Finalize or EvidenceReanalysisNodeTypes.Finalize or PortfolioDiagnosisNodeTypes.FinalizeDiagnosis));
        if (proposal.GoalStatus == DynamicGoalStatuses.Complete && !finalizationCompleted && (review?[CriticReviewFields.RequiresRevision]?.GetValue<bool>() == true || review?[CriticReviewFields.RequiresMoreEvidence]?.GetValue<bool>() == true)) throw new InvalidOperationException("Dynamic plan cannot complete while Critic requirements remain unresolved.");
        if (proposal.GoalStatus == DynamicGoalStatuses.Continue && proposal.Actions.Count == 0) throw new InvalidOperationException("A continuing plan must contain actions.");
        var initialNodeCount = run.Nodes.Count(x => string.IsNullOrWhiteSpace(x.TemplateNodeKey));
        var dynamicBudget = run.WorkflowType switch
        {
            AgentWorkflowTypes.PortfolioDiagnosis => PortfolioDiagnosisWorkflow.MaxDynamicNodes,
            AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision => ResearchQualityReviewWorkflow.MaxDynamicNodes + ResearchInvestigationWorkflow.MaxInitialPlanNodes,
            _ => ResearchQualityReviewWorkflow.MaxDynamicNodes
        };
        if (run.Nodes.Count + proposal.Actions.Count > initialNodeCount + dynamicBudget) throw new InvalidOperationException("Dynamic node budget exceeded.");
        var webOccurrences = run.Nodes.Count(x => x.NodeType is EvidenceRemediationNodeTypes.RetrieveWebEvidence or ResearchInvestigationNodeTypes.RetrieveWeb)
            + proposal.Actions.Count(x => x.NodeType is EvidenceRemediationNodeTypes.RetrieveWebEvidence or ResearchInvestigationNodeTypes.RetrieveWeb);
        if (webOccurrences > 1) throw new InvalidOperationException("Web retrieval budget exceeded.");
        var request = board[AgentBlackboardKeys.ResearchRequest]?.Deserialize<EquityLens.Api.Contracts.Research.ResearchAskRequest>(AgentNodeJson.SerializerOptions);
        if (request is not null && !SourcePolicyRules.AllowsWeb(request.SourcePolicy)
            && proposal.Actions.Any(x => x.NodeType is EvidenceRemediationNodeTypes.RetrieveWebEvidence or ResearchInvestigationNodeTypes.RetrieveWeb))
            throw new InvalidOperationException("Source policy LocalOnly forbids Web retrieval.");
        if (request is not null && !SourcePolicyRules.AllowsLocal(request.SourcePolicy)
            && proposal.Actions.Any(x => x.NodeType is EvidenceRemediationNodeTypes.RetrieveEvidence or ResearchInvestigationNodeTypes.RetrieveLocal))
            throw new InvalidOperationException("Source policy WebOnly forbids local retrieval.");
        ValidateCapabilityRequestGate(run, proposal, board, request);
        var duplicate = proposal.Actions.GroupBy(x => x.ClientNodeKey, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1)?.Key;
        if (duplicate is not null) throw new InvalidOperationException($"Dynamic plan contains duplicate node key '{duplicate}'.");
        var existing = run.Nodes.Select(x => x.NodeKey).ToHashSet(StringComparer.Ordinal);
        if (proposal.Actions.Any(x => existing.Contains(x.ClientNodeKey))) throw new InvalidOperationException("Dynamic plan attempts to replace an existing node.");
        var availableDependencies = existing.Concat(proposal.Actions.Select(x => x.ClientNodeKey)).ToHashSet(StringComparer.Ordinal);
        var availableKeys = board.Where(x => x.Value is not null).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var action in proposal.Actions)
        {
            NodeCapability capability;
            try { capability = capabilities.Get(action.Capability); } catch (InvalidOperationException) { throw new InvalidOperationException($"Unknown capability '{action.Capability}'."); }
            if (capability.NodeType != action.NodeType) throw new InvalidOperationException($"Capability '{action.Capability}' does not map to node type '{action.NodeType}'.");
            if (action.DependsOn.Count == 0 || action.DependsOn.Any(x => !availableDependencies.Contains(x))) throw new InvalidOperationException($"Node '{action.ClientNodeKey}' has an unknown or empty dependency.");
            var occurrences = action.NodeType == PortfolioRiskMathNodeTypes.Execute
                ? run.Nodes.Count(x => x.TemplateNodeKey == action.Capability) + proposal.Actions.Count(x => x.Capability == action.Capability)
                : run.Nodes.Count(x => x.NodeType == action.NodeType) + proposal.Actions.Count(x => x.NodeType == action.NodeType);
            if (occurrences > capability.MaxOccurrences) throw new InvalidOperationException($"Capability '{action.Capability}' occurrence budget exceeded.");
            if (action.NodeType == PortfolioRiskMathNodeTypes.Execute)
            {
                if (proposal.Actions.Count(x => x.NodeType == PortfolioRiskMathNodeTypes.Execute) > 8) throw new InvalidOperationException("A plan may contain at most eight math capabilities.");
                if (capability.InputMode is "MultiAsset" or "Matrix" && request?.PortfolioId is null && board[AgentBlackboardKeys.PortfolioId] is null) throw new InvalidOperationException($"Math capability '{action.Capability}' requires a portfolioId.");
                try { PortfolioRiskMathCapabilities.ParseArguments(action.Capability, action.Arguments); }
                catch (AgentNodeException exception) { throw new InvalidOperationException(exception.Message, exception); }
            }
            if (action.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence)
            {
                ValidateSearchIntents(action.Arguments, 8, requireTargets: false);
                if (action.Arguments["allowWebFallback"]?.GetValue<bool>() != false) throw new InvalidOperationException("Dynamic local retrieval must disable hidden Web fallback.");
            }
            if (action.NodeType == EvidenceRemediationNodeTypes.RetrieveWebEvidence) ValidateSearchIntents(action.Arguments, 5, requireTargets: true);
            var contract = catalog.GetNode(action.NodeType).Contract;
            var missing = contract.RequiredBlackboardKeys.FirstOrDefault(x => !(action.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence && x == AgentBlackboardKeys.RetrievalPlan) && !availableKeys.Contains(x));
            if (missing is not null) throw new InvalidOperationException($"Node '{action.NodeType}' requires unavailable blackboard key '{missing}'.");
            availableKeys.UnionWith(contract.ProducedBlackboardKeys);
        }
        var definition = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject().DeepClone().AsObject();
        var nodes = definition["nodes"]!.AsArray(); var edges = definition["edges"]!.AsArray();
        foreach (var action in proposal.Actions) { nodes.Add(new JsonObject { ["id"] = action.ClientNodeKey, ["type"] = action.NodeType }); foreach (var dependency in action.DependsOn) edges.Add(new JsonObject { ["from"] = dependency, ["to"] = action.ClientNodeKey }); }
        topology.GetExecutionOrder(definition.ToJsonString());
        ValidateInitialResearchPlan(run, proposal, request);
        ValidateFeedbackRevisionPlan(proposal);
        ValidatePortfolioDiagnosisPlan(run, proposal, board);
        ValidateWebPlacement(run, proposal.Actions);
        return new(proposal, proposal.Actions);
    }

    private static void ValidateFeedbackRevisionPlan(DynamicPlanProposal proposal)
    {
        if (proposal.Trigger != DynamicPlanningTriggers.FeedbackContextReady) return;
        string[] required = [ResearchInvestigationNodeTypes.DraftAnswer, ResearchQualityReviewNodeTypes.BuildEvidencePacket,
            ResearchQualityReviewNodeTypes.CheckEvidence, ResearchQualityReviewNodeTypes.CritiqueAnswer,
            ResearchQualityReviewNodeTypes.FinalizeCriticReport];
        foreach (var type in required)
            if (proposal.Actions.Count(x => x.NodeType == type) != 1)
                throw new InvalidOperationException($"Feedback revision plan must contain exactly one '{type}' node.");
        var actions = proposal.Actions.ToDictionary(x => x.ClientNodeKey, StringComparer.Ordinal);
        var previous = FeedbackRevisionNodeKeys.ValidateContext;
        foreach (var type in required)
        {
            var action = proposal.Actions.Single(x => x.NodeType == type);
            if (!DependsTransitivelyOn(action, previous, actions))
                throw new InvalidOperationException($"Feedback revision node '{action.ClientNodeKey}' must depend on the preceding required stage.");
            previous = action.ClientNodeKey;
        }
    }

    private static void ValidatePortfolioDiagnosisPlan(AgentRun run, DynamicPlanProposal proposal, JsonObject board)
    {
        if (run.WorkflowType != AgentWorkflowTypes.PortfolioDiagnosis) return;
        var allowedTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            PortfolioRiskMathNodeTypes.Execute,
            PortfolioDiagnosisNodeTypes.EvaluateQuality,
            PortfolioDiagnosisNodeTypes.BuildEvidencePacket,
            PortfolioDiagnosisNodeTypes.DraftDiagnosis,
            PortfolioDiagnosisNodeTypes.FinalizeDiagnosis
        };
        var unauthorized = proposal.Actions.FirstOrDefault(x => !allowedTypes.Contains(x.NodeType));
        if (unauthorized is not null)
            throw new InvalidOperationException($"Portfolio diagnosis plan cannot use node type '{unauthorized.NodeType}'.");
        if (proposal.GoalStatus == DynamicGoalStatuses.Complete) return;

        var math = proposal.Actions.Where(x => x.NodeType == PortfolioRiskMathNodeTypes.Execute).ToList();
        var evaluators = proposal.Actions.Where(x => x.NodeType == PortfolioDiagnosisNodeTypes.EvaluateQuality).ToList();
        var finalTypes = new[] { PortfolioDiagnosisNodeTypes.BuildEvidencePacket, PortfolioDiagnosisNodeTypes.DraftDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeDiagnosis };
        var hasFinal = proposal.Actions.Any(x => finalTypes.Contains(x.NodeType, StringComparer.Ordinal));
        if (math.Count > 0)
        {
            if (math.Count > PortfolioDiagnosisWorkflow.MaxMathCapabilitiesPerIteration)
                throw new InvalidOperationException("Portfolio diagnosis math capability budget exceeded.");
            var quality = board[AgentBlackboardKeys.PortfolioDiagnosisQuality]
                ?.Deserialize<PortfolioDiagnosisQualityResult>(AgentNodeJson.SerializerOptions)
                ?? throw new InvalidOperationException("Portfolio diagnosis quality result is missing.");
            var expected = quality.RequiredCapabilities.Order(StringComparer.Ordinal).ToList();
            var actual = math.Select(x => x.Capability).Order(StringComparer.Ordinal).ToList();
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
                throw new InvalidOperationException("Portfolio diagnosis plan must execute exactly the capabilities required by the quality gate.");
            if (evaluators.Count != 1 || hasFinal)
                throw new InvalidOperationException("Portfolio diagnosis analysis plan must end with exactly one quality evaluator.");
            var evaluator = evaluators[0];
            if (math.Any(x => !evaluator.DependsOn.Contains(x.ClientNodeKey, StringComparer.Ordinal)))
                throw new InvalidOperationException("Portfolio diagnosis quality evaluator must depend on every planned math node.");
            return;
        }

        if (evaluators.Count > 0 || proposal.Actions.Count != 3)
            throw new InvalidOperationException("Portfolio diagnosis final plan must contain exactly packet, draft, and finalization nodes.");
        var actions = proposal.Actions.ToDictionary(x => x.ClientNodeKey, StringComparer.Ordinal);
        var previous = run.Nodes
            .Where(x => x.Status == AgentNodeStatuses.Succeeded && x.NodeType == PortfolioDiagnosisNodeTypes.EvaluateQuality)
            .OrderByDescending(x => x.Iteration)
            .ThenByDescending(x => x.CompletedAtUtc)
            .FirstOrDefault()?.NodeKey
            ?? throw new InvalidOperationException("Portfolio diagnosis final plan requires a completed quality gate.");
        foreach (var type in finalTypes)
        {
            var action = proposal.Actions.SingleOrDefault(x => x.NodeType == type)
                ?? throw new InvalidOperationException($"Portfolio diagnosis final plan is missing '{type}'.");
            if (!DependsTransitivelyOn(action, previous, actions))
                throw new InvalidOperationException($"Portfolio diagnosis node '{action.ClientNodeKey}' must depend on the preceding finalization stage.");
            previous = action.ClientNodeKey;
        }
    }

    private static void ValidateInitialResearchPlan(AgentRun run, DynamicPlanProposal proposal, EquityLens.Api.Contracts.Research.ResearchAskRequest? request)
    {
        if (proposal.Trigger != DynamicPlanningTriggers.ResearchContextReady) return;
        var requiredTypes = new List<string> { ResearchInvestigationNodeTypes.PlanRetrieval };
        if (request?.SourcePolicy != EquityLens.Api.Contracts.Research.SourcePolicy.WebOnly) requiredTypes.Add(ResearchInvestigationNodeTypes.RetrieveLocal);
        requiredTypes.Add(ResearchInvestigationNodeTypes.EvaluateEvidence);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var gated = request is not null && DeterministicDynamicWorkflowPlanner.IsConferenceCapabilityGate(board, request);
        if (!gated)
            requiredTypes.AddRange([ResearchInvestigationNodeTypes.RankEvidence, ResearchInvestigationNodeTypes.DraftAnswer, ResearchQualityReviewNodeTypes.BuildEvidencePacket, ResearchQualityReviewNodeTypes.CheckEvidence, ResearchQualityReviewNodeTypes.CritiqueAnswer, ResearchQualityReviewNodeTypes.FinalizeCriticReport]);
        foreach (var type in requiredTypes)
            if (proposal.Actions.Count(x => x.NodeType == type) != 1) throw new InvalidOperationException($"Initial research plan must contain exactly one '{type}' node.");
        if (gated && proposal.Actions.Count != requiredTypes.Count)
            throw new InvalidOperationException("Capability-gated initial research plan must stop after evidence evaluation.");

        var needsWeb = request?.SourcePolicy is EquityLens.Api.Contracts.Research.SourcePolicy.WebOnly or EquityLens.Api.Contracts.Research.SourcePolicy.LocalAndWeb or EquityLens.Api.Contracts.Research.SourcePolicy.LocalThenWeb
            || request?.SourcePolicy == EquityLens.Api.Contracts.Research.SourcePolicy.Auto && ResearchInvestigationPlanning.IsFreshnessSensitive(request.Question);
        if (!gated && needsWeb && proposal.Actions.Count(x => x.NodeType == ResearchInvestigationNodeTypes.RetrieveWeb) != 1)
            throw new InvalidOperationException("Initial research plan requires a Web retrieval capability.");

        var actions = proposal.Actions.ToDictionary(x => x.ClientNodeKey, StringComparer.Ordinal);
        var orderedTypes = requiredTypes.ToList();
        if (proposal.Actions.Any(x => x.NodeType == ResearchInvestigationNodeTypes.RetrieveWeb))
            orderedTypes.Insert(orderedTypes.IndexOf(ResearchInvestigationNodeTypes.EvaluateEvidence) + 1, ResearchInvestigationNodeTypes.RetrieveWeb);
        var previousKey = ResearchInvestigationNodeKeys.DetectIntent;
        foreach (var type in orderedTypes)
        {
            var action = proposal.Actions.Single(x => x.NodeType == type);
            if (!DependsTransitivelyOn(action, previousKey, actions)) throw new InvalidOperationException($"Initial research node '{action.ClientNodeKey}' must depend on the preceding research stage.");
            previousKey = action.ClientNodeKey;
        }
    }

    private static void ValidateCapabilityRequestGate(
        AgentRun run,
        DynamicPlanProposal proposal,
        JsonObject board,
        EquityLens.Api.Contracts.Research.ResearchAskRequest? request)
    {
        if (proposal.Trigger != DynamicPlanningTriggers.CapabilityRequestsReady) return;
        if (board[AgentBlackboardKeys.LeadSkill]?.GetValue<string>() != "conference-call-takeaways")
            throw new InvalidOperationException("Capability request planning is limited to conference-call-takeaways.");
        var pending = DeterministicDynamicWorkflowPlanner.PendingWebRequest(board);
        var webCount = proposal.Actions.Count(x => x.NodeType == ResearchInvestigationNodeTypes.RetrieveWeb);
        if (webCount > 0 && pending is null)
            throw new InvalidOperationException("Web retrieval requires a Pending capability request.");
        string[] required =
        [
            ResearchInvestigationNodeTypes.RankEvidence,
            ResearchInvestigationNodeTypes.DraftAnswer,
            ResearchQualityReviewNodeTypes.BuildEvidencePacket,
            ResearchQualityReviewNodeTypes.CheckEvidence,
            ResearchQualityReviewNodeTypes.CritiqueAnswer,
            ResearchQualityReviewNodeTypes.FinalizeCriticReport
        ];
        foreach (var type in required)
            if (proposal.Actions.Count(x => x.NodeType == type) != 1)
                throw new InvalidOperationException($"Capability request continuation must contain exactly one '{type}' node.");
        var permittedCount = required.Length + webCount;
        if (proposal.Actions.Count != permittedCount)
            throw new InvalidOperationException("Capability request continuation contains an unauthorized node.");
        if (request is not null && !SourcePolicyRules.AllowsWeb(request.SourcePolicy) && webCount > 0)
            throw new InvalidOperationException("Source policy LocalOnly forbids Web retrieval.");
        var actions = proposal.Actions.ToDictionary(x => x.ClientNodeKey, StringComparer.Ordinal);
        var ordered = webCount == 1 ? new[] { ResearchInvestigationNodeTypes.RetrieveWeb }.Concat(required) : required;
        var previous = run.Nodes.Single(x => x.NodeType == ResearchInvestigationNodeTypes.EvaluateEvidence && x.Status == AgentNodeStatuses.Succeeded).NodeKey;
        foreach (var type in ordered)
        {
            var action = proposal.Actions.Single(x => x.NodeType == type);
            if (!DependsTransitivelyOn(action, previous, actions))
                throw new InvalidOperationException($"Capability continuation node '{action.ClientNodeKey}' must depend on the preceding stage.");
            previous = action.ClientNodeKey;
        }
    }

    private static bool DependsTransitivelyOn(DynamicPlanAction action, string dependency, IReadOnlyDictionary<string, DynamicPlanAction> actions)
    {
        var pending = new Stack<string>(action.DependsOn);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var key))
        {
            if (key == dependency) return true;
            if (visited.Add(key) && actions.TryGetValue(key, out var parent)) foreach (var item in parent.DependsOn) pending.Push(item);
        }
        return false;
    }

    private static void ValidateSearchIntents(JsonObject arguments, int maximumTopK, bool requireTargets)
    {
        if (arguments["searchIntents"] is not JsonArray intents || intents.Count is < 1 or > 3) throw new InvalidOperationException("RetrieveEvidence requires 1-3 searchIntents.");
        foreach (var item in intents.OfType<JsonObject>())
        {
            if (item["topic"]?.GetValue<string>() is not { Length: > 0 }) throw new InvalidOperationException("searchIntent.topic is required.");
            var topK = item["topK"]?.GetValue<int>() ?? 0; if (topK < 1 || topK > maximumTopK) throw new InvalidOperationException($"searchIntent.topK must be between 1 and {maximumTopK}.");
            var freshness = item["freshness"]?.GetValue<string>(); if (freshness is not (null or "day" or "week" or "month" or "year")) throw new InvalidOperationException("searchIntent.freshness is invalid.");
            if (requireTargets && (item["targetClaims"] is not JsonArray targets || targets.Count == 0)) throw new InvalidOperationException("Web searchIntent requires targetClaims.");
        }
    }

    private static void ValidateWebPlacement(AgentRun run, IReadOnlyList<DynamicPlanAction> actions)
    {
        var web = actions.SingleOrDefault(x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveWebEvidence);
        if (web is null) return;
        var byKey = actions.ToDictionary(x => x.ClientNodeKey, StringComparer.Ordinal);
        bool HasAncestor(string key, Func<string, bool> predicate, HashSet<string>? visited = null)
        {
            visited ??= [];
            if (!visited.Add(key)) return false;
            if (predicate(key)) return true;
            return byKey.TryGetValue(key, out var action) && action.DependsOn.Any(x => HasAncestor(x, predicate, visited));
        }
        bool IsExtraction(string key) => byKey.TryGetValue(key, out var action)
            ? action.NodeType == EvidenceRemediationNodeTypes.ExtractClaims
            : run.Nodes.Any(x => x.NodeKey == key && x.NodeType == EvidenceRemediationNodeTypes.ExtractClaims && x.Status == AgentNodeStatuses.Succeeded);
        if (!web.DependsOn.Any(x => HasAncestor(x, IsExtraction))) throw new InvalidOperationException("Web retrieval must depend on claim extraction.");
        foreach (var assessor in actions.Where(x => x.NodeType == EvidenceRemediationNodeTypes.AssessSupport))
            if (!assessor.DependsOn.Any(x => HasAncestor(x, key => key == web.ClientNodeKey))) throw new InvalidOperationException("Evidence assessment must depend on Web retrieval when Web retrieval is planned.");
    }
}

public interface IGraphMaterializer { void Materialize(AgentRun run, ValidatedDynamicPlan plan); }
public sealed class GraphMaterializer(EquityLensDbContext db, IAgentWorkflowCatalog catalog) : IGraphMaterializer
{
    public void Materialize(AgentRun run, ValidatedDynamicPlan validated)
    {
        if (run.OrchestrationVersion != validated.Proposal.BaseOrchestrationVersion) throw new InvalidOperationException("Dynamic plan became stale before materialization.");
        var definition = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject(); var nodes = definition["nodes"]!.AsArray(); var edges = definition["edges"]!.AsArray();
        AgentRunNode? last = null;
        foreach (var action in validated.Actions)
        {
            var contract = catalog.GetNode(action.NodeType).Contract;
            var node = new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = action.ClientNodeKey, TemplateNodeKey = action.Capability, Iteration = action.Iteration, NodeType = action.NodeType, Status = AgentNodeStatuses.Pending, InputJson = action.Arguments.ToJsonString(AgentNodeJson.SerializerOptions) };
            run.Nodes.Add(node);
            db.AgentRunNodes.Add(node);
            last = node;
            nodes.Add(new JsonObject { ["id"] = action.ClientNodeKey, ["templateNodeKey"] = action.Capability, ["type"] = action.NodeType, ["iteration"] = action.Iteration, ["required"] = true, ["plannedArguments"] = action.Arguments.DeepClone(), ["condition"] = action.Condition is null ? null : new JsonObject { ["path"] = action.Condition.Path, ["equals"] = action.Condition.ExpectedValue }, ["executionPolicy"] = new JsonObject { ["timeoutSeconds"] = contract.DefaultPolicy.TimeoutSeconds, ["maxRetryCount"] = contract.DefaultPolicy.MaxRetryCount } });
            foreach (var dependency in action.DependsOn) edges.Add(new JsonObject { ["from"] = dependency, ["to"] = action.ClientNodeKey });
        }
        var patches = definition["planningHistory"] as JsonArray ?? new JsonArray(); patches.Add(JsonSerializer.SerializeToNode(new { validated.Proposal.ProposalId, validated.Proposal.Trigger, validated.Proposal.GoalStatus, validated.Proposal.Reason, validated.Proposal.SelectedSkills, addedNodes = validated.Actions.Select(x => x.ClientNodeKey), baseOrchestrationVersion = validated.Proposal.BaseOrchestrationVersion }, AgentNodeJson.SerializerOptions)); definition["planningHistory"] = patches;
        definition["goalStatus"] = validated.Proposal.GoalStatus; definition["orchestrationMode"] = "DynamicStateful"; run.WorkflowDefinitionJson = definition.ToJsonString(AgentNodeJson.SerializerOptions);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        if (validated.Proposal.Trigger == DynamicPlanningTriggers.CapabilityRequestsReady)
        {
            var approved = validated.Actions.Any(x => x.NodeType == ResearchInvestigationNodeTypes.RetrieveWeb);
            if (board[AgentBlackboardKeys.CapabilityRequests] is JsonArray requests)
            {
                foreach (var request in requests.OfType<JsonObject>().Where(x => x["status"]?.GetValue<string>() == "Pending"))
                {
                    request["status"] = approved ? "Approved" : "Rejected";
                    request["reviewedAtUtc"] = DateTime.UtcNow;
                    request["reviewReason"] = validated.Proposal.Reason;
                    AddEvent(run, approved ? AgentEventTypes.CapabilityRequestApproved : AgentEventTypes.CapabilityRequestRejected,
                        approved ? "Planner and Validator approved Web research capability." : "Planner declined Web research capability.",
                        new { requestId = request["requestId"]?.GetValue<Guid>(), capabilityId = request["capabilityId"]?.GetValue<string>(), validated.Proposal.Reason });
                }
            }
            if (board[AgentBlackboardKeys.InitialEvidencePolicy] is JsonObject policy) policy["useWeb"] = approved;
        }
        if (last is not null) board["dynamicLastNodeKey"] = last.NodeKey; run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        run.OrchestrationVersion++;
        AddEvent(run, AgentEventTypes.GraphMaterialized, $"Materialized {validated.Actions.Count} dynamic nodes.", new { validated.Proposal.ProposalId, validated.Proposal.Trigger, nodes = validated.Actions.Select(x => x.ClientNodeKey), run.OrchestrationVersion });
        var definitionVersion = definition["version"]?.GetValue<int>() ?? 1;
        if (last is not null) db.AgentRunWakeOutbox.Add(new AgentRunWakeOutbox { Id = Guid.NewGuid(), AgentRunId = run.Id, UserId = run.UserId, WorkflowType = run.WorkflowType, AgentRunNodeId = last.Id, DefinitionVersion = definitionVersion, OrchestrationVersion = run.OrchestrationVersion, CorrelationId = run.Id, CausationId = validated.Proposal.ProposalId });
    }
    private void AddEvent(AgentRun run, string type, string message, object payload) => db.AgentRunEvents.Add(new AgentRunEvent { Id = Guid.NewGuid(), AgentRunId = run.Id, EventType = type, Message = message, PayloadJson = AgentNodeJson.Serialize(payload), CreatedAtUtc = DateTime.UtcNow });
}
