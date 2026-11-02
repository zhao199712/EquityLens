# Phase 1 Agent Runtime Handoff - 2026-07-09

## Purpose

This handoff captures the completed Phase 1 backend work for moving agent workflow execution out of synchronous HTTP requests and into a Redis Stream backed background runtime.

Phase 1 is now implemented, tested, pushed to GitHub, and merged so both `main` and `backend` point to commit:

```text
8efa8a8 Add Redis-backed agent run worker
```

## What Changed

Agent run creation is now asynchronous from the API caller's perspective.

Previous behavior:

```text
POST /api/agent-runs/critic-review
 -> create AgentRun
 -> execute full workflow inside the request
 -> return final Succeeded/Failed result
```

Current behavior:

```text
POST /api/agent-runs/critic-review
 -> create AgentRun with Pending status
 -> enqueue Redis Stream job
 -> return Pending run immediately
 -> AgentRunWorker executes workflow in background
 -> frontend polls GET /api/agent-runs/{runId}
```

The same pattern applies to DraftRevision and retry.

## Runtime Architecture

Current execution path:

```text
AgentRunsController
 -> AgentRunService
    -> create/reset AgentRun
    -> persist RunCreated / retry event
    -> enqueue AgentRunQueueMessage
 -> RedisAgentRunQueue
    -> XADD to Redis Stream
 -> AgentRunWorker
    -> reclaim stale pending message first
    -> read new message second
    -> call AgentRunExecutor
    -> XACK after successful executor return
 -> AgentRunExecutor
    -> state transitions
    -> workflow planning / graph validation
    -> node handler execution
    -> persisted events / tool calls / blackboard / output
```

Ownership is now split clearly:

- `AgentRunService`: create, list, get detail, retry, cancel, enqueue only.
- `AgentRunExecutor`: the only workflow execution path.
- `RedisAgentRunQueue`: Redis Stream enqueue/read/ack/reclaim adapter.
- `AgentRunWorker`: hosted background loop.

## New Interfaces and Options

### `IAgentRunExecutor`

```csharp
Task ExecuteAsync(Guid runId, Guid userId, CancellationToken cancellationToken = default);
```

Executes one persisted agent run. It skips runs that are no longer `Pending`, which protects against duplicate Redis delivery or reclaim attempts.

### `IAgentRunQueue`

```csharp
Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default);
Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default);
Task<AgentRunQueueItem?> ReadStalePendingAsync(string consumerName, TimeSpan minIdleTime, CancellationToken cancellationToken = default);
Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default);
```

### `AgentRunQueueOptions`

Configured under `AgentRunQueue`:

```json
{
  "AgentRunQueue": {
    "StreamKey": "equitylens:agent-runs:stream",
    "ConsumerGroupName": "agent-run-workers",
    "PendingMinIdleSeconds": 300
  }
}
```

Defaults:

```text
StreamKey = equitylens:agent-runs:stream
ConsumerGroupName = agent-run-workers
PendingMinIdleSeconds = 300
```

`PendingMinIdleSeconds` is a crash recovery timeout. It does not delay new jobs. A job is reclaimable only if a worker already read it and did not ack it within the configured idle window.

## Redis Stream Behavior

Normal path:

```text
XADD stream
XREADGROUP stream >
AgentRunExecutor.ExecuteAsync(...)
XACK stream messageId
```

Recovery path:

```text
worker reads message
worker crashes before XACK
message remains pending
another worker uses XAUTOCLAIM after PendingMinIdleSeconds
executor skips if run is no longer Pending
successful executor return triggers XACK
```

Current reclaim implementation uses:

```text
XAUTOCLAIM stream group consumer min-idle 0-0 COUNT 1
```

The worker checks stale pending messages before reading new messages.

## API Behavior for Frontend

Create endpoints now return `Pending` immediately:

```http
POST /api/agent-runs/critic-review
POST /api/agent-runs/draft-revision
POST /api/agent-runs/{runId}/retry
```

Frontend should not expect create responses to contain completed output. The intended UX is:

```text
create run
 -> receive runId with Pending status
 -> navigate to run detail or start polling
 -> poll GET /api/agent-runs/{runId}
 -> render run status, node timeline, events, tool calls, output
```

Polling is enough for Phase 1. Redis Pub/Sub and SSE were intentionally not added.

Expected status flow:

```text
Pending -> Running -> Succeeded
Pending -> Running -> Failed
Pending/Running -> Cancelled
```

Expected CriticReview nodes:

```text
loadResearchRun
buildEvidencePacket
checkEvidence
critiqueAnswer
finalizeCriticReport
```

Expected DraftRevision nodes:

```text
loadCriticReviewRun
draftRevisedAnswer
finalizeRevision
```

## Tests and Validation

Automated tests run successfully:

```text
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj
296 passed
```

Targeted agent tests also passed during development:

```text
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj --filter Agents
75 passed

dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj --filter AgentRunWorkerTests
5 passed
```

Real API flow was tested successfully after options were added:

```text
POST /api/Auth/register
POST /api/research/ask
POST /api/agent-runs/critic-review
GET  /api/agent-runs/{runId}
```

Observed result:

```text
critic-review create response: Pending
final run status: Succeeded
events: 32
toolCalls: 2
all CriticReview nodes: Succeeded
log: no Redis/worker/exception errors
```

Example final output from the real flow:

```json
{
  "overallSeverity": "None",
  "requiresRevision": false,
  "requiresMoreEvidence": false,
  "recommendedNextAction": "AcceptAnswer"
}
```

## Important Notes

- `src/EquityLens.Api/appsettings.json` is not tracked by git. It was used for local testing only.
- `AGENTS.md` had an unrelated local modification and was intentionally excluded from the Phase 1 commit.
- `main` and `backend` are both pushed to GitHub at commit `8efa8a8`.
- The worker does not use Pub/Sub. DB remains the source of truth for frontend progress.
- `AgentRunExecutor` catches workflow exceptions and marks the run `Failed`; worker then acks because executor returned. If the process crashes before executor returns, Redis pending recovery handles it later.

## Frontend Next Step

The next frontend task is to make the UI treat agent run creation as asynchronous.

Minimum expected frontend behavior:

- After `POST /api/agent-runs/critic-review`, use returned `id` immediately.
- Show `Pending` state instead of waiting for final output.
- Poll `GET /api/agent-runs/{runId}` until terminal status.
- Render node timeline from `nodes`.
- Render events/tool calls/output as they appear.
- Handle `Failed` and `Cancelled` states explicitly.

Suggested UX flow:

```text
ResearchRun detail
 -> click Run Critic Review
 -> create AgentRun
 -> navigate to AgentRun detail
 -> timeline updates by polling
 -> output appears when Succeeded
```

Do not add SSE or Redis Pub/Sub for the frontend yet unless polling is insufficient in practice.
