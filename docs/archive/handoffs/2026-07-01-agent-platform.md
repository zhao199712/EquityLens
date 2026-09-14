# EquityLens V3 Agent Platform Handoff - Updated 2026-07-08

## Purpose

This handoff is compressed context for the current V3 Agent Platform state. The original 2026-07-01 skeleton has been advanced into a backend-only product chain with persisted DAG runtime, LLM CriticReview, LLM DraftRevision, policy routing, tool-call observability, and E2E validation.

## Current Goal

Build V3 Agent Platform incrementally while keeping the runtime debuggable and testable. The current backend chain validates:

```text
ResearchRun
 -> CriticReview
 -> policy decision
 -> DraftRevision
```

MAF is still deferred. The product runtime owns persisted DAG execution for now.

## Runtime Architecture

Current execution path:

```text
AgentRunsController
 -> AgentRunService
    -> AgentStateMachine run transition
    -> workflow provider registry
    -> AgentWorkflowPlanner
    -> AgentRunGraphValidator
    -> AgentStateMachine node transitions
    -> IAgentNodeHandler.ExecuteAsync(...)
    -> BlackboardJson / OutputJson / AgentRunEvent / AgentToolCall persistence
```

Core runtime concepts:

- Workflow: persisted task graph definition in `WorkflowDefinitionJson`.
- Provider: creates initial `AgentRun`, `AgentRunNode` list, workflow definition JSON, and initial blackboard JSON.
- Planner: derives execution order from DAG edges via topological sort.
- Validator: verifies persisted node keys match planned node keys exactly.
- Handler: executes one node and writes node output / blackboard updates.
- Blackboard: shared JSON state between nodes.
- Policy evaluator: deterministic backend routing decision seam.
- Tool call: persisted external/tool/model call observability.
- State machine: centralized run/node status transition guard and retry reset path.

## Current Workflows

### CriticReview

```text
loadResearchRun
 -> buildEvidencePacket
 -> checkEvidence
 -> critiqueAnswer
 -> finalizeCriticReport
```

Purpose: review an existing V2 `ResearchRun` answer and evidence quality.

Current behavior:

- `loadResearchRun`: loads V2 `ResearchRunDetailDto` via `IResearchRunTraceService` and writes `researchRun`, `answer`, `citations`, `steps`, and `candidates` to blackboard.
- `buildEvidencePacket`: converts raw trace shape into stable `evidencePacket` contract.
- `checkEvidence`: deterministic evidence/citation checks from `evidencePacket`.
- `critiqueAnswer`: calls `ICriticReviewAgent`; production DI uses `LlmCriticReviewAgent`.
- `finalizeCriticReport`: combines critic result with `CriticReviewPolicyEvaluator` decision.

### DraftRevision

```text
loadCriticReviewRun
 -> draftRevisedAnswer
 -> finalizeRevision
```

Purpose: revise an answer based on a completed owner-visible CriticReview run.

Current behavior:

- `loadCriticReviewRun`: loads completed CriticReview `AgentRun` for the same user.
- `draftRevisedAnswer`: calls `IDraftRevisionAgent`; production DI uses `LlmDraftRevisionAgent` when revision is required.
- `finalizeRevision`: writes final DraftRevision output.

## Agent Implementations

### Critic Agent

Production:

```csharp
ICriticReviewAgent -> LlmCriticReviewAgent
```

Tests:

```csharp
ICriticReviewAgent -> DeterministicCriticReviewAgent / fakes
```

`LlmCriticReviewAgent` uses `IChatCompletionService` with:

```text
ResponseFormat = JsonObject
Temperature = 0.1
MaxTokens = 4096
```

LLM output schema:

```json
{
  "summary": "string",
  "overallSeverity": "None|Low|Medium|High|Critical",
  "findings": [
    {
      "severity": "Low|Medium|High|Critical",
      "category": "UnsupportedClaim|WeakCitation|MissingCitation|InsufficientEvidence|Contradiction|Overclaim|MissingAnswer|General",
      "message": "string",
      "relatedCitationIndexes": [1],
      "recommendation": "string"
    }
  ],
  "suggestedAnswerRevision": "string|null"
}
```

The LLM does not decide platform routing. Routing fields are produced by policy evaluator.

Important hardening: deterministic evidence findings from `checkEvidence` are merged into the LLM result and cannot be dropped by the LLM.

### Draft Agent

Production:

```csharp
IDraftRevisionAgent -> LlmDraftRevisionAgent
```

Tests:

```csharp
IDraftRevisionAgent -> DeterministicDraftRevisionAgent
```

`LlmDraftRevisionAgent` uses `IChatCompletionService` with JSON output when `RequiresRevision=true`. It short-circuits without LLM when `RequiresRevision=false`.

LLM output schema:

```json
{
  "revisedAnswer": "string",
  "revisionSummary": "string",
  "appliedRecommendation": "string"
}
```

## DeepSeek JSON Output

`ChatCompletionRequest` now supports:

```csharp
public enum ChatResponseFormat
{
    Text,
    JsonObject
}
```

DeepSeek adapter sends:

```json
"response_format": { "type": "json_object" }
```

when `ResponseFormat == ChatResponseFormat.JsonObject`.

Gemini currently throws `NotSupportedException` for `JsonObject`; do not silently ignore JSON mode.

## Policy Decision Seam

Added:

```text
WorkflowPolicyContext
WorkflowPolicyDecision
IWorkflowPolicyEvaluator
CriticReviewPolicyEvaluator
```

Current CriticReview policy rules:

- Evidence issue categories (`MissingCitation`, `InsufficientEvidence`, `WeakCitation`) route to `ResearchRetrieval`.
- Other findings route to `AnswerGeneration`.
- No findings route to `AcceptAnswer`.

Final CriticReview output still contains:

```json
{
  "summary": "...",
  "overallSeverity": "High",
  "findings": [],
  "requiresRevision": true,
  "requiresMoreEvidence": true,
  "routeBackTo": "ResearchRetrieval",
  "recommendedNextAction": "CollectMoreEvidenceThenReviseAnswer",
  "suggestedAnswerRevision": null
}
```

## Observability Contract

AgentRun detail exposes:

```text
run
nodes
events
toolCalls
feedback
blackboardJson
outputJson
workflowDefinitionJson
```

Important `agent_tool_call` records now include:

```text
getResearchRun
criticReviewLLM
```

`criticReviewLLM` arguments include ticker, citation/candidate counts, source status, evidence finding count, and answer preview.

`draftRevisionLLM` arguments include ticker, severity, finding count, source answer preview, and applied recommendation baseline.

Both LLM tool calls persist status, duration, result preview, result JSON, and error message on failure.

Aspire / OpenTelemetry:

- `Program.cs` exports `EquityLens.Api` traces, metrics, and logs to the configured OTLP endpoint; `docker-compose.yml` includes Aspire Dashboard on `${ASPIRE_DASHBOARD_PORT:-18888}`.
- `AgentRunService` emits `agent.run.execute` and `agent.node.execute` spans with run, workflow, node key, and node type tags.
- `AgentStateMachine` emits Activity events for `agent.run.status.transition`, `agent.run.status.reset`, `agent.node.status.transition`, and `agent.node.status.reset`.
- Metrics counters `equitylens.agent.run.status.transitions` and `equitylens.agent.node.status.transitions` expose low-cardinality transition counts by workflow/node type and from/to status.
- For debugging a specific run, use API persisted `events` / `nodes` / `toolCalls` as the source of truth; use Aspire to inspect latency, failures, and cross-service request traces.

## Blackboard Contract

Core CriticReview blackboard keys:

```json
{
  "ticker": null,
  "question": null,
  "researchRunId": null,
  "researchRun": null,
  "answer": null,
  "citations": [],
  "steps": [],
  "candidates": [],
  "evidencePacket": null,
  "evidenceChecks": {
    "citationCount": 0,
    "candidateCount": 0,
    "sourceStatus": "",
    "findingCount": 0,
    "findings": []
  },
  "criticFindings": [],
  "criticReview": null,
  "supervisorDecisions": [],
  "finalOutput": null
}
```

Core DraftRevision blackboard keys:

```json
{
  "criticReviewRunId": null,
  "criticReviewRun": null,
  "ticker": null,
  "question": null,
  "answer": null,
  "criticFindings": [],
  "criticReview": null,
  "revisedAnswer": null,
  "revisionSummary": null,
  "appliedRecommendation": null,
  "finalOutput": null
}
```

## Important Files

Runtime and contracts:

```text
src/EquityLens.Api/Services/Agents/AgentRunService.cs
src/EquityLens.Api/Services/Agents/IAgentRunService.cs
src/EquityLens.Api/Services/Agents/IAgentNodeHandler.cs
src/EquityLens.Api/Services/Agents/AgentNodeExecutionContext.cs
src/EquityLens.Api/Services/Agents/AgentNodeJson.cs
src/EquityLens.Api/Services/Agents/AgentWorkflowConstants.cs
src/EquityLens.Api/Services/Agents/AgentWorkflowDefinitionProvider.cs
src/EquityLens.Api/Services/Agents/AgentWorkflowPlanner.cs
src/EquityLens.Api/Services/Agents/AgentRunGraphValidator.cs
src/EquityLens.Api/Services/Agents/AgentStateMachine.cs
src/EquityLens.Api/Services/Agents/AgentBlackboardContracts.cs
src/EquityLens.Api/Services/Agents/AgentNodeContracts.cs
src/EquityLens.Api/Services/Agents/WorkflowPolicyEvaluator.cs
```

Observability:

```text
src/EquityLens.Api/Observability/EquityLensTelemetry.cs
```

Agents and node handlers:

```text
src/EquityLens.Api/Services/Agents/CriticReviewAgent.cs
src/EquityLens.Api/Services/Agents/LlmCriticReviewAgent.cs
src/EquityLens.Api/Services/Agents/DraftRevisionAgent.cs
src/EquityLens.Api/Services/Agents/CriticReviewNodeHandlers.cs
src/EquityLens.Api/Services/Agents/DraftRevisionNodeHandlers.cs
```

Chat JSON mode:

```text
src/EquityLens.Api/Services/Ai/IChatCompletionService.cs
src/EquityLens.Api/Services/Ai/DeepSeekChatCompletionService.cs
src/EquityLens.Api/Services/Ai/GeminiChatCompletionService.cs
```

API:

```text
src/EquityLens.Api/Controllers/AgentRunsController.cs
src/EquityLens.Api/Contracts/Agents/AgentRunContracts.cs
```

Persistence:

```text
src/EquityLens.Api/Data/Entities/AgentRun.cs
src/EquityLens.Api/Data/Entities/AgentRunNode.cs
src/EquityLens.Api/Data/Entities/AgentRunEvent.cs
src/EquityLens.Api/Data/Entities/AgentToolCall.cs
src/EquityLens.Api/Data/Entities/AgentFeedback.cs
src/EquityLens.Api/Data/Configurations/AgentRunConfiguration.cs
src/EquityLens.Api/Data/Configurations/AgentToolCallConfiguration.cs
src/EquityLens.Api/Migrations/20260630071302_AddAgentRunTables.cs
src/EquityLens.Api/Migrations/20260707091108_FixAgentRunUserRelationship.cs
```

Tests:

```text
tests/EquityLens.Api.Tests/Services/Agents/AgentRunServiceTests.cs
tests/EquityLens.Api.Tests/Services/Agents/AgentStateMachineTests.cs
tests/EquityLens.Api.Tests/Services/Agents/AgentWorkflowPlannerTests.cs
tests/EquityLens.Api.Tests/Services/Agents/AgentRunGraphValidatorTests.cs
tests/EquityLens.Api.Tests/Services/Agents/LlmCriticReviewAgentTests.cs
tests/EquityLens.Api.Tests/Services/Agents/LlmDraftRevisionAgentTests.cs
tests/EquityLens.Api.Tests/Services/Ai/DeepSeekChatCompletionServiceTests.cs
```

## API Surface

```text
POST /api/agent-runs/critic-review
POST /api/agent-runs/draft-revision
GET  /api/agent-runs
GET  /api/agent-runs/{runId}
POST /api/agent-runs/{runId}/retry
POST /api/agent-runs/{runId}/cancel
```

All endpoints are authorized and use `ICurrentUserContext.UserId` for isolation.

## Database / Migration Notes

E2E exposed an EF shadow FK issue where EF tried to insert `AppUserId` into `agent_run`. Fixed by explicitly mapping:

```csharp
builder.HasOne<AppUser>()
    .WithMany(u => u.AgentRuns)
    .HasForeignKey(x => x.UserId)
    .OnDelete(DeleteBehavior.Cascade);
```

Migration added:

```text
20260707091108_FixAgentRunUserRelationship
```

It only adds:

```text
FK_agent_run_app_user_user_id
agent_run.user_id -> app_user.id
```

No `AppUserId` column is added.

## E2E Validation Results

Happy path query:

```text
台積電最近年報提到哪些主要營運風險？
```

Result:

```text
ResearchRun: Answered
CriticReview: Succeeded
CriticReview policy: AcceptAnswer
```

Weak-evidence query:

```text
台積電年報是否提到火星殖民計畫以及相關資本支出金額？請列出具體金額。
```

Result:

```text
ResearchRun: InsufficientEvidence
CriticReview: Succeeded
CriticReview policy: CollectMoreEvidenceThenReviseAnswer
DraftRevision: Succeeded
```

Latest DraftRevision E2E output:

```json
{
  "sourceAnswer": "目前提供的資料不足以回答此問題。文件中未提及任何關於火星殖民計畫或相關資本支出金額的資訊。",
  "revisedAnswer": "目前提供的資料中，台積電年報未提及任何關於火星殖民計畫或相關資本支出金額的資訊。",
  "revisionSummary": "已確認原始回答的資料不足結論，未作實質修改。",
  "revisionRequired": true,
  "appliedRecommendation": "補強引用支撐並避免超出來源證據，回答已限制在無相關資訊的結論。"
}
```

## Verification Status

Latest commands:

```text
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj --filter FullyQualifiedName~Agent
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj
dotnet build src/EquityLens.Api/EquityLens.Api.csproj
ConnectionStrings__PostgreSQL="..." ~/.dotnet/tools/dotnet-ef database update --project src/EquityLens.Api --startup-project src/EquityLens.Api
API E2E: register -> /api/research/ask -> /api/agent-runs/critic-review -> /api/agent-runs/draft-revision
```

Latest results:

```text
Focused Agent tests: 74 passed, 0 failed
Tests: 290 passed, 0 failed
Backend build: success
EF database update: already up to date
E2E: ResearchRun InsufficientEvidence -> CriticReview Succeeded -> DraftRevision Succeeded
```

Known warnings:

```text
NU1510 System.Text.Encoding.CodePages
Vite chunk size > 500 kB
```

Both warnings pre-existed this platform work.

## Rules For Future Expansion

When adding a workflow:

1. Add workflow/agent/node constants in `AgentWorkflowConstants.cs`.
2. Add blackboard keys/fields if shared state changes.
3. Add node input/output contracts in `AgentNodeContracts.cs`.
4. Add an `IAgentWorkflowDefinitionProvider` implementation.
5. Add one `IAgentNodeHandler` per node.
6. Register provider, handlers, agents, policy evaluators, and any state-machine/observability dependencies in `Program.cs`.
7. Add service/API method or generic create endpoint.
8. Add tests for workflow definition, planner order, handler coverage, blackboard/output contract, failure/retry behavior, and user isolation.

Rules:

- Do not hardcode execution order in `AgentRunService`; keep DAG edges in `WorkflowDefinitionJson`.
- Do not assign `AgentRun.Status` or `AgentRunNode.Status` directly in runtime code; use `AgentStateMachine.Transition` or `ResetForRetry`.
- Keep node handlers focused on business logic and JSON/tool-call writes; status lifecycle belongs to `AgentRunService` plus `AgentStateMachine`.
- Keep deterministic implementations/fakes for tests.
- Keep LLM implementations behind agent interfaces.
- Do not let LLM decide platform route fields directly; use policy evaluators.
- Persist every external/model call as `AgentToolCall`.
- Keep MAF deferred behind seams until product runtime is stable.

## Next Recommended Backend Steps

1. Add explicit `draftRevisionLLM` failure/retry service test with a flaky `IDraftRevisionAgent`.
2. Add E2E or integration-style coverage for `requiresRevision=false` DraftRevision short-circuit with no LLM call.
3. Consider introducing a generic workflow create endpoint only after another workflow is added.
4. Consider scheduler-level conditional routing later: auto-start DraftRevision or ResearchRetrieval based on `WorkflowPolicyDecision`.
5. Keep frontend work separate unless explicitly requested.
