# V3 Agent Platform Contract v0

## V3 目標

把 V2 的單次 `/api/research/ask` 升級成：

- 可追蹤的 Agent Run
- 可觀測的 Execution Timeline
- 可重試的 Node / Tool Call
- 可存取的 Blackboard
- 可擴充的 Workflow DAG
- 可接 Supervisor / Human Feedback

第一個落地 workflow：

- **CriticReview**：輸入一個既有 `researchRunId`，檢查回答品質、citation 覆蓋、證據不足、風險與改進建議。

---

## 1. AgentRun 狀態機

狀態：

```
Pending
Running
WaitingForFeedback
Succeeded
Failed
Cancelled
```

允許轉移：

```
Pending -> Running
Pending -> Cancelled

Running -> Succeeded
Running -> Failed
Running -> WaitingForFeedback
Running -> Cancelled

WaitingForFeedback -> Running
WaitingForFeedback -> Cancelled

Failed -> Pending
```

語意：

| 狀態 | 說明 |
|------|------|
| Pending | 任務已建立，尚未開始 |
| Running | 任務執行中 |
| WaitingForFeedback | 等待使用者或人工審核輸入 |
| Succeeded | 成功完成 |
| Failed | 執行失敗 |
| Cancelled | 被取消 |

Retry 策略：`Failed -> Pending`（重跑整個 run），node-level retry 後續再補。

---

## 2. AgentNode 狀態機

每個 DAG node 的執行狀態。

狀態：

```
Pending
Ready
Running
Succeeded
Failed
Skipped
WaitingForFeedback
```

允許轉移：

```
Pending -> Ready
Ready -> Running

Running -> Succeeded
Running -> Failed
Running -> Skipped
Running -> WaitingForFeedback

WaitingForFeedback -> Ready

Failed -> Ready
```

語意：

| 狀態 | 說明 |
|------|------|
| Pending | 依賴尚未滿足 |
| Ready | 可以執行 |
| Running | 執行中 |
| Succeeded | 執行成功 |
| Failed | 執行失敗 |
| Skipped | 被 supervisor 或條件略過 |
| WaitingForFeedback | 此 node 需要人工輸入 |

---

## 3. DB Contract

### agent_run

代表一次 agent workflow 執行。

| 欄位 | 型別 | 備註 |
|------|------|------|
| id | uuid PK | |
| user_id | uuid NOT NULL | |
| workflow_type | text NOT NULL | e.g. CriticReview |
| agent_type | text NOT NULL | e.g. CriticAgent |
| status | text NOT NULL | |
| input_json | jsonb NOT NULL | |
| output_json | jsonb NULL | |
| blackboard_json | jsonb NOT NULL | |
| workflow_definition_json | jsonb NOT NULL | |
| error_message | text NULL | |
| created_at_utc | timestamptz NOT NULL | |
| started_at_utc | timestamptz NULL | |
| completed_at_utc | timestamptz NULL | |

範例 input_json：

```json
{ "researchRunId": "..." }
```

---

### agent_run_node

DAG node instance。

| 欄位 | 型別 | 備註 |
|------|------|------|
| id | uuid PK | |
| agent_run_id | uuid NOT NULL | FK |
| node_key | text NOT NULL | e.g. loadResearchRun |
| node_type | text NOT NULL | e.g. LoadResearchRun |
| status | text NOT NULL | |
| input_json | jsonb NULL | |
| output_json | jsonb NULL | |
| error_message | text NULL | |
| started_at_utc | timestamptz NULL | |
| completed_at_utc | timestamptz NULL | |
| duration_ms | bigint NULL | |

---

### agent_run_event

Execution Timeline 的核心表。

| 欄位 | 型別 | 備註 |
|------|------|------|
| id | uuid PK | |
| agent_run_id | uuid NOT NULL | FK |
| agent_run_node_id | uuid NULL | FK |
| event_type | text NOT NULL | |
| message | text NULL | |
| payload_json | jsonb NULL | |
| created_at_utc | timestamptz NOT NULL | |

Event 類型：

```
RunCreated
RunStarted
RunSucceeded
RunFailed
RunCancelled

NodeReady
NodeStarted
NodeCompleted
NodeFailed
NodeSkipped

ToolCallStarted
ToolCallCompleted
ToolCallFailed

BlackboardUpdated
SupervisorDecision

FeedbackRequested
FeedbackReceived
```

範例：

```json
{
  "eventType": "SupervisorDecision",
  "message": "Supervisor selected critiqueAnswer as next node.",
  "payload": {
    "nextNode": "critiqueAnswer",
    "reason": "Evidence coverage check completed."
  }
}
```

---

### agent_tool_call

每次 tool call 紀錄。

| 欄位 | 型別 | 備註 |
|------|------|------|
| id | uuid PK | |
| agent_run_id | uuid NOT NULL | FK |
| agent_run_node_id | uuid NULL | FK |
| tool_name | text NOT NULL | |
| status | text NOT NULL | Running / Succeeded / Failed / Cancelled |
| arguments_json | jsonb NOT NULL | |
| result_preview | text NULL | |
| result_json | jsonb NULL | |
| error_message | text NULL | |
| started_at_utc | timestamptz NOT NULL | |
| completed_at_utc | timestamptz NULL | |
| duration_ms | bigint NULL | |

第一批 tools：

```
getResearchRun
searchDocuments
queryFinancialData
webSearch
```

---

### agent_feedback

Human feedback 預留。

| 欄位 | 型別 | 備註 |
|------|------|------|
| id | uuid PK | |
| agent_run_id | uuid NOT NULL | FK |
| agent_run_node_id | uuid NULL | FK |
| feedback_type | text NOT NULL | |
| status | text NOT NULL | Requested / Submitted / Expired / Cancelled |
| prompt | text NOT NULL | |
| response_json | jsonb NULL | |
| created_at_utc | timestamptz NOT NULL | |
| responded_at_utc | timestamptz NULL | |

---

## 4. Blackboard Contract

第一版存在 `agent_run.blackboard_json`，不拆表。

CriticReview v0 blackboard：

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

criticFindings 項目：

```json
[
  {
    "severity": "High",
    "category": "UnsupportedClaim",
    "message": "回答提到毛利率下滑主因，但引用內容未直接支持此結論。",
    "relatedCitationIndexes": [1, 3],
    "recommendation": "補充營收成本、產能利用率或產品組合相關證據。"
  }
]
```

Severity `Low | Medium | High | Critical`

Category `UnsupportedClaim | WeakCitation | MissingCitation | Contradiction | InsufficientEvidence | OverconfidentAnswer | StaleEvidence | NumericalMismatch`

---

## 5. Workflow / DAG Definition

第一版用 JSON 存在 `agent_run.workflow_definition_json`，不拆 node/edge 表。

CriticReview v0：

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

第一版限制：**不支援 parallel node、任意 expression condition、user-defined code node、graph editor**。

---

## 6. Supervisor Contract

第一版為「受 DAG 約束的決策器」，不是完全自由 LLM 決策。

輸入：

```json
{
  "agentRunId": "...",
  "workflowDefinition": {},
  "blackboard": {},
  "nodeStatuses": []
}
```

輸出：

```json
{
  "decision": "RunNode",
  "nextNodeId": "checkEvidence",
  "reason": "loadResearchRun completed and dependencies are satisfied."
}
```

Decision 類型 `RunNode | WaitForFeedback | CompleteRun | FailRun | CancelRun | SkipNode`

第一版 **deterministic**（根據 DAG dependency + node status 決定），後續再升級 LLM supervisor。

---

## 7. Tool Contract

第一版只定 interface，不做 UI registry。

Tool metadata 概念：

```json
{
  "name": "getResearchRun",
  "description": "Load persisted research run detail by ID.",
  "inputSchema": { "researchRunId": "uuid" },
  "outputSchema": { "run": "object", "steps": "array", "candidates": "array", "citations": "array" },
  "timeoutSeconds": 30,
  "mutatesBlackboard": true
}
```

---

## 8. CriticReview v0 Workflow

入口 `POST /api/agent-runs/critic-review`

Request：

```json
{ "researchRunId": "..." }
```

Response：

```json
{ "id": "...", "status": "Pending" }
```

流程：

```
RunCreated
RunStarted

loadResearchRun
  -> tool: getResearchRun
  -> blackboard.researchRun = result

checkEvidence
  -> inspect answer + citations
  -> blackboard.evidenceChecks = result

critiqueAnswer
  -> ICriticReviewAgent deterministic seam in v0, LLM implementation later
  -> blackboard.criticReview = typed critic result
  -> blackboard.criticFindings = criticReview.findings

finalizeCriticReport
  -> blackboard.finalOutput = critic report
  -> agent_run.output_json = finalOutput

RunSucceeded
```

Final output：

```json
{
  "summary": "回答大致有根據，但部分結論引用支撐不足。",
  "overallSeverity": "Medium",
  "findings": [
    {
      "severity": "Medium",
      "category": "WeakCitation",
      "message": "...",
      "relatedCitationIndexes": [],
      "recommendation": "..."
    }
  ],
  "requiresRevision": true,
  "requiresMoreEvidence": true,
  "routeBackTo": "ResearchRetrieval",
  "recommendedNextAction": "CollectMoreEvidenceThenReviseAnswer",
  "suggestedAnswerRevision": "..."
}
```

---

## 9. API Contract

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/agent-runs/critic-review` | 建立 CriticReview run |
| GET | `/api/agent-runs?limit=&workflowType=&status=` | 列出 runs |
| GET | `/api/agent-runs/{runId}` | run detail（含 nodes/events/toolCalls/blackboard/output） |
| POST | `/api/agent-runs/{runId}/retry` | retry 整個 run |
| POST | `/api/agent-runs/{runId}/cancel` | 取消 run |

---

## 10. 跟 MAF 的邊界

已安裝 `Microsoft.Agents.AI`，但第一版不讓 domain code 直接依賴 MAF。

包一層：

```csharp
public interface IAgentWorkflowRunner
{
    Task<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);
}
```

第一版實作 `EquityLensAgentWorkflowRunner`，之後需要時再接 `MafAgentWorkflowRunner`。  
DB contract、API contract、前端 timeline 不用重寫。

---

## 11. 建議實作順序

1. **Entities + Migration**：AgentRun / AgentRunNode / AgentRunEvent / AgentToolCall / AgentFeedback + EF config + migration
2. **Service + Controller**：建立 CriticReview run、List、Detail、Retry、Cancel API
3. **CriticReview runner**：實際 workflow 執行、blackboard 更新、event timeline、tool call logging
4. **Frontend**：Agent Run List、Agent Run Detail、Execution Timeline、Tool Calls、Blackboard viewer

---

## 12. 需要你確認的決策

- [ ] 狀態機是否足夠？
- [ ] Blackboard 第一版放 `agent_run.blackboard_json` 是否可接受？
- [ ] workflow_definition_json 先不拆 node/edge table 是否可接受？
- [ ] Supervisor 第一版 deterministic，不先接 LLM？
- [ ] Retry 第一版 retry 整個 run？
- [ ] CriticReview 作為第一個 V3 workflow？
