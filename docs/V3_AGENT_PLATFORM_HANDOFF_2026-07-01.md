# EquityLens V3 Agent Platform Handoff - 2026-07-01

## Purpose

This handoff summarizes the V3 Agent Platform backend skeleton work completed on 2026-07-01. It is intended as compressed context for opening a fresh coding session without losing the decisions, implementation state, and next steps.

## Current Goal

Build the V3 Agent Platform incrementally. The first slice intentionally uses only a `CriticAgent` workflow to validate the platform runtime before expanding to full multi-agent orchestration.

The first backend workflow is:

```text
loadResearchRun
 -> checkEvidence
 -> critiqueAnswer
 -> finalizeCriticReport
```

This is a DAG/workflow graph definition implemented by the product runtime, not by MAF.

## Key Architecture Decision

The first version does not use the MAF runtime yet.

Current execution path:

```text
AgentRunsController
 -> AgentRunService
    -> loadResearchRun
    -> checkEvidence
    -> ICriticReviewAgent.CritiqueAsync
    -> finalizeCriticReport
```

MAF is intentionally deferred. The current purpose is to validate our own product runtime first:

- `AgentRun`
- `AgentRunNode`
- `AgentRunEvent`
- `AgentToolCall`
- `BlackboardJson`
- `OutputJson`
- workflow definition persistence
- retry/cancel semantics
- user isolation
- node timeline/debuggability

Future MAF integration should happen behind seams, for example:

```text
ICriticReviewAgent -> MafCriticReviewAgent
```

or later:

```text
IAgentWorkflowRunner -> MafAgentWorkflowRunner
```

## Why Start With Only CriticAgent

The complete V3 vision includes Supervisor, state machine, MoE routing, DAG execution, blackboard, multiple agents, critic loop, and final answer generation. Implementing all of that at once makes failures hard to isolate.

The `CriticReview` workflow is a vertical slice. It consumes existing V2 `ResearchRun` data and validates whether the V3 runtime can persist, execute, observe, fail, retry, and produce a stable output contract.

It validates:

- run creation
- node execution
- node status/timing/error capture
- event timeline
- tool call logging
- blackboard read/write
- output contract
- failure handling
- retry handling
- user isolation

## Contract Layers

Three contract layers are now separated.

### 1. Workflow / DAG Contract

Defines graph structure: workflow type, version, nodes, and edges.

Main file:

```text
src/EquityLens.Api/Services/Agents/AgentWorkflowConstants.cs
```

Important constants:

```csharp
AgentWorkflowTypes.CriticReview
AgentTypes.Critic
CriticReviewWorkflow.Version
CriticReviewNodeKeys.*
CriticReviewNodeTypes.*
```

Current DAG:

```json
{
  "workflowType": "CriticReview",
  "version": 1,
  "nodes": [
    { "id": "loadResearchRun", "type": "LoadResearchRun", "required": true },
    { "id": "checkEvidence", "type": "CheckEvidence", "required": true },
    { "id": "critiqueAnswer", "type": "CritiqueAnswer", "required": true },
    { "id": "finalizeCriticReport", "type": "FinalizeCriticReport", "required": true }
  ],
  "edges": [
    { "from": "loadResearchRun", "to": "checkEvidence" },
    { "from": "checkEvidence", "to": "critiqueAnswer" },
    { "from": "critiqueAnswer", "to": "finalizeCriticReport" }
  ]
}
```

### 2. Blackboard Contract

Defines the shared JSON artifacts nodes read/write.

Main file:

```text
src/EquityLens.Api/Services/Agents/AgentBlackboardContracts.cs
```

Includes:

```csharp
AgentBlackboardKeys
EvidenceCheckFields
CriticReviewFields
CriticFindingFields
AgentBlackboardContracts.CreateInitialCriticReviewBlackboard(...)
AgentBlackboardContracts.CreateEvidenceChecks(...)
AgentBlackboardContracts.CreateFinalOutput(...)
AgentBlackboardContracts.CreateFinding(...)
```

Core blackboard keys:

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

### 3. Node Input/Output Contract

Defines the persisted `AgentRunNode.InputJson` and `AgentRunNode.OutputJson` shapes.

Main file:

```text
src/EquityLens.Api/Services/Agents/AgentNodeContracts.cs
```

Current contracts:

```csharp
LoadResearchRunNodeInput
LoadResearchRunNodeOutput
CheckEvidenceNodeInput
CheckEvidenceNodeOutput
FinalizeCriticReportNodeInput
FinalizeCriticReportNodeOutput
```

The `critiqueAnswer` node uses the existing agent-level contracts:

```text
src/EquityLens.Api/Services/Agents/CriticReviewAgent.cs
```

```csharp
CriticReviewInput
CriticReviewResult
CriticFinding
```

## Important Implementation Files

### Backend Runtime

```text
src/EquityLens.Api/Services/Agents/AgentRunService.cs
src/EquityLens.Api/Services/Agents/IAgentRunService.cs
src/EquityLens.Api/Services/Agents/AgentWorkflowConstants.cs
src/EquityLens.Api/Services/Agents/AgentBlackboardContracts.cs
src/EquityLens.Api/Services/Agents/AgentNodeContracts.cs
src/EquityLens.Api/Services/Agents/CriticReviewAgent.cs
```

### API

```text
src/EquityLens.Api/Controllers/AgentRunsController.cs
src/EquityLens.Api/Contracts/Agents/AgentRunContracts.cs
```

### Persistence

```text
src/EquityLens.Api/Data/Entities/AgentRun.cs
src/EquityLens.Api/Data/Entities/AgentRunNode.cs
src/EquityLens.Api/Data/Entities/AgentRunEvent.cs
src/EquityLens.Api/Data/Entities/AgentToolCall.cs
src/EquityLens.Api/Data/Entities/AgentFeedback.cs
src/EquityLens.Api/Data/Configurations/AgentRunConfiguration.cs
src/EquityLens.Api/Data/Configurations/AgentRunNodeConfiguration.cs
src/EquityLens.Api/Data/Configurations/AgentRunEventConfiguration.cs
src/EquityLens.Api/Data/Configurations/AgentToolCallConfiguration.cs
src/EquityLens.Api/Data/Configurations/AgentFeedbackConfiguration.cs
src/EquityLens.Api/Migrations/20260630071302_AddAgentRunTables.cs
```

### ResearchRun Trace Dependency

```text
src/EquityLens.Api/Services/Research/IResearchRunTraceService.cs
src/EquityLens.Api/Services/Research/ResearchRunTraceService.cs
```

`ResearchRunDetailDto` now exposes `Answer`, and `AgentRunService` writes it to `blackboard["answer"]`.

### Tests

```text
tests/EquityLens.Api.Tests/Services/Agents/AgentRunServiceTests.cs
tests/EquityLens.Api.Tests/Controllers/AgentRunsControllerTests.cs
```

## What Was Implemented In This Session

### CriticReview Fourth Node

Added `critiqueAnswer` as a real DAG node:

```text
loadResearchRun -> checkEvidence -> critiqueAnswer -> finalizeCriticReport
```

`critiqueAnswer` calls:

```csharp
ICriticReviewAgent.CritiqueAsync(...)
```

First implementation:

```csharp
DeterministicCriticReviewAgent
```

It does not call an LLM yet.

### Final Output Contract

`finalOutput` now includes:

```json
{
  "summary": "...",
  "overallSeverity": "Medium",
  "findings": [],
  "requiresRevision": true,
  "requiresMoreEvidence": true,
  "routeBackTo": "ResearchRetrieval",
  "recommendedNextAction": "CollectMoreEvidenceThenReviseAnswer",
  "suggestedAnswerRevision": "..."
}
```

### Workflow Contract Hardening

Added constants and tests to lock:

- workflow type
- version
- node list
- node order
- node types
- edges
- retry preserving workflow definition

### Blackboard Contract Hardening

Added centralized blackboard contract and tests to lock:

- required top-level keys
- `evidenceChecks` schema
- `criticReview` schema
- `finalOutput` schema
- `blackboard.finalOutput == run.OutputJson`

### Node Contract Hardening

Added typed node contracts for:

- `loadResearchRun` input/output
- `checkEvidence` input/output
- `finalizeCriticReport` input/output

`critiqueAnswer` already uses typed agent contracts.

### Failure And Retry Tests

Added `FlakyCriticReviewAgent` test behavior:

- first `critiqueAnswer` call throws `critic unavailable`
- run becomes `Failed`
- `critiqueAnswer` node becomes `Failed`
- `OutputJson` remains null
- retry replays workflow
- retry succeeds
- `WorkflowDefinitionJson` remains unchanged

### Authorization / User Isolation Tests

Added service tests confirming another user cannot:

- retry someone else's run
- cancel someone else's run

Both return null and do not mutate the owner run.

## Current Test Status

Command run:

```text
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj
```

Result:

```text
Passed: 225
Failed: 0
Skipped: 0
```

Known warning still present:

```text
NU1510 System.Text.Encoding.CodePages
```

This warning existed before this slice and was not introduced by the V3 Agent Platform changes.

## Current Git / Workspace State

Branch at time of handoff:

```text
backend
```

Remote:

```text
origin git@github.com:zhao199712/EquityLens.git
```

Important note: the working tree contains many existing modified and untracked files from broader V3/research work. Do not blindly commit everything unless that is intentional.

For the handoff commit, only this file should be staged/committed unless explicitly requested otherwise.

## Next Recommended Backend Steps

### 1. Extract Node Execution Handlers

Current `AgentRunService` still dispatches nodes internally. Next step should be to move each node into a handler seam:

```csharp
IAgentNodeHandler
LoadResearchRunNodeHandler
CheckEvidenceNodeHandler
CritiqueAnswerNodeHandler
FinalizeCriticReportNodeHandler
```

Goal: prevent `AgentRunService` from becoming a large switch/case god service as more nodes/agents are added.

### 2. Keep MAF Deferred

Do not introduce MAF yet. The next clean MAF insertion point is likely:

```csharp
ICriticReviewAgent -> MafCriticReviewAgent
```

not replacing the whole runtime.

### 3. Add Real LLM Critic Later

Once node handlers are extracted and contracts remain green, add an LLM implementation behind:

```csharp
ICriticReviewAgent
```

Keep `DeterministicCriticReviewAgent` for tests.

### 4. Then Add A Second Agent

After `CriticReview` is stable, choose one:

- `DraftAgent` for answer revision
- `ResearchAgent` wrapper for evidence expansion

Avoid adding Supervisor/MoE first. They should come after at least two agents can run independently.

## Rules For Future Expansion

When adding a new DAG node:

1. Add node key/type constants.
2. Update workflow definition nodes/edges.
3. Update created `AgentRunNode` list.
4. Add node input/output records.
5. Add blackboard keys/fields if it writes shared artifacts.
6. Add tests for workflow contract.
7. Add tests for blackboard contract.
8. Add tests for failure/retry behavior.

When only changing an agent implementation behind an existing interface, workflow contract usually does not need to change.

