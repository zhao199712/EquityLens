# EquityLens V3 Agent Platform Contract

> 來源：PM 提供的文字合約 + 架構示意圖
> 狀態：草案，待對齊

---

## 一、V3 目標

把 V2 的單次 `/api/research/ask` 升級成：

- 可追蹤的 Agent Run
- 可觀測的 Execution Timeline
- 可重試的 Node / Tool Call
- 可存取的 Blackboard
- 可擴充的 Workflow DAG
- 可接 Supervisor / Human Feedback

第一個落地 workflow：**CriticReview** — 輸入一個既有 `researchRunId`，檢查回答品質、citation 覆蓋、證據不足、風險與改進建議。

---

## 二、架構示意圖（圖片內容）

### 整體流程

```
使用者 Query
    │
    ▼
Orchestrator / Supervisor ──→ 狀態機 (Workflow State Machine)
    │                              │
    │                              ▼
    │                         各階段狀態流轉
    │
    ▼
MoE Router / Expert Selection ──→ Workflow Graph / DAG
    │                                    │
    │                                    ▼
    │                              Blackboard / Artifact Store
    │                                    │
    │                                    ▼
    │                              Critic Loop / 品質關門
    │                                    │
    │                                    ▼
    │                              最終答案
```

### 各組件說明

#### 1. Orchestrator / Supervisor
- 控制流程
- 決定下一步
- 驗證輸出
- 控制停止條件

#### 2. 狀態機 (Workflow State Machine)

```
Created → Planning → CollectingEvidence → Synthesizing → Critiquing → Completed
                          ↘                    ↗
                    NeedsRevision    NeedsHumanReview
```

- 管 run 的生命週期
- 適合合法轉移、重試、人工審查

#### 3. MoE Router / Expert Selection

選擇適合的 experts（並非每個查詢都會使用全部專家）：

| Expert | 用途 |
|--------|------|
| ResearchRagAgent | 研究檢索 / RAG |
| FinancialStatementAgent | 財務報表分析 |
| PortfolioAnalystAgent | 投組曝險分析 |
| RiskAnalystAgent | 風險分析與假說 |
| CriticAgent | 批判與品質把關 |

#### 4. Workflow Graph / DAG

DAG 管：哪些工作節點可以並行、哪些必須等待依賴完成。

```
Planner
    │
    ├──→ ResearchAgent/RAG ──────┐
    ├──→ FinancialDataTool ──────┤→ 合流 → RiskAgent → DraftAgent → CriticAgent → FinalAnswer
    └──→ PortfolioTool ──────────┘
```

#### 5. Blackboard / Artifact Store

存放共享中間成果，所有代理與工具讀寫共享成果，確保一致性與可追溯性：

| Artifact | 說明 |
|----------|------|
| ResearchFinding | 研究發現 |
| FinancialMetricFinding | 財務指標發現 |
| PortfolioExposureFinding | 投組曝險發現 |
| RiskHypothesis | 風險假說 |
| CritiqueIssue | 批評議題 |
| FinalClaim | 最終主張 / 結論 |

#### 6. Critic Loop / 品質關門

```
CriticAgent
    ├──→ Pass ──────────────────────→ 最終答案
    ├──→ Needs Revision ────────────→ 回到 DAG（修改後重新提交）
    └──→ Needs More Research ───────→ 回到 DAG（補充研究後重新提交）
```

檢查主張品質與論證完整性，避免 overclaim、檢查 citation 與 source attribution。

#### 7. 最終答案

結構化輸出：

| 欄位 | 說明 |
|------|------|
| Answer | 論述主張與觀點依據 |
| Citations | 來源清單與引用 |
| Trace / RunID | 可追蹤的執行記錄 |
| 信心指標 (Confidence) | Primary source ✓ / Financial data ✓ / Critic pass ✓ |

### 關鍵概念差異

| | 狀態機 (Workflow State Machine) | DAG (Workflow Graph) |
|---|---|---|
| 管什麼 | run 的生命週期 | 工作節點依賴 |
| 適合 | 合法轉移、重試、人工審查 | 並行、合流、局部重跑 |

### 圖例

| 顏色 | 意義 |
|------|------|
| 淺藍色框 | 狀態機（流程控制） |
| 綠色框 | MoE / 專家選擇 |
| 紫色框 | DAG（工作依賴） |
| 淺黃色框 | 共享區（資料、成果） |
| 橘色框 | 品質關門（把關） |
| 淺綠色框 | 最終輸出（結果） |

---

## 三、文字合約內容

### 1. AgentRun 狀態機

```
Pending → Running → Succeeded
                  → Failed
                  → WaitingForFeedback
                  → Cancelled

Failed → Pending（retry）
WaitingForFeedback → Running
```

| 狀態 | 說明 |
|------|------|
| Pending | 任務已建立，尚未開始 |
| Running | 任務執行中 |
| WaitingForFeedback | 等待使用者或人工審核輸入 |
| Succeeded | 成功完成 |
| Failed | 執行失敗 |
| Cancelled | 被取消 |

Retry 策略：`Failed → Pending`（重跑整個 run），node-level retry 後續再補。

### 2. AgentNode 狀態機

```
Pending → Ready → Running → Succeeded
                        → Failed
                        → Skipped
                        → WaitingForFeedback

WaitingForFeedback → Ready
Failed → Ready
```

| 狀態 | 說明 |
|------|------|
| Pending | 依賴尚未滿足 |
| Ready | 可以執行 |
| Running | 執行中 |
| Succeeded | 執行成功 |
| Failed | 執行失敗 |
| Skipped | 被 supervisor 或條件略過 |
| WaitingForFeedback | 此 node 需要人工輸入 |

### 3. DB Contract

#### agent_run

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

#### agent_run_node

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

#### agent_run_event

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
RunCreated / RunStarted / RunSucceeded / RunFailed / RunCancelled
NodeReady / NodeStarted / NodeCompleted / NodeFailed / NodeSkipped
ToolCallStarted / ToolCallCompleted / ToolCallFailed
BlackboardUpdated / SupervisorDecision
FeedbackRequested / FeedbackReceived
```

#### agent_tool_call

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

第一批 tools：`getResearchRun` / `searchDocuments` / `queryFinancialData` / `webSearch`

#### agent_feedback

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

### 4. Blackboard Contract

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
    "missingCitationClaims": [],
    "weakEvidenceClaims": [],
    "unsupportedClaims": []
  },
  "criticFindings": [],
  "supervisorDecisions": [],
  "finalOutput": null
}
```

criticFindings 項目：
```json
{
  "severity": "High | Medium | Low | Critical",
  "category": "UnsupportedClaim | WeakCitation | MissingCitation | Contradiction | InsufficientEvidence | OverconfidentAnswer | StaleEvidence | NumericalMismatch",
  "message": "...",
  "relatedCitationIndexes": [1, 3],
  "recommendation": "..."
}
```

### 5. Workflow / DAG Definition

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

### 6. Supervisor Contract

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

Decision 類型：`RunNode | WaitForFeedback | CompleteRun | FailRun | CancelRun | SkipNode`

第一版 **deterministic**（根據 DAG dependency + node status 決定），後續再升級 LLM supervisor。

### 7. Tool Contract

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

### 8. CriticReview v0 Workflow

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
RunCreated → RunStarted

loadResearchRun
  → tool: getResearchRun
  → blackboard.researchRun = result

checkEvidence
  → inspect answer + citations
  → blackboard.evidenceChecks = result

critiqueAnswer
  → LLM / rule-based critic
  → blackboard.criticFindings = result

finalizeCriticReport
  → blackboard.finalOutput = critic report
  → agent_run.output_json = finalOutput

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
      "recommendation": "..."
    }
  ],
  "suggestedAnswerRevision": "..."
}
```

### 9. API Contract

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/agent-runs/critic-review` | 建立 CriticReview run |
| GET | `/api/agent-runs?limit=&workflowType=&status=` | 列出 runs |
| GET | `/api/agent-runs/{runId}` | run detail（含 nodes/events/toolCalls/blackboard/output） |
| POST | `/api/agent-runs/{runId}/retry` | retry 整個 run |
| POST | `/api/agent-runs/{runId}/cancel` | 取消 run |

### 10. 跟 MAF 的邊界

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

第一版實作 `EquityLensAgentWorkflowRunner`，之後需要時再接 `MafAgentWorkflowRunner`。DB contract、API contract、前端 timeline 不用重寫。

### 11. 建議實作順序

1. **Entities + Migration**：AgentRun / AgentRunNode / AgentRunEvent / AgentToolCall / AgentFeedback + EF config + migration
2. **Service + Controller**：建立 CriticReview run、List、Detail、Retry、Cancel API
3. **CriticReview runner**：實際 workflow 執行、blackboard 更新、event timeline、tool call logging
4. **Frontend**：Agent Run List、Agent Run Detail、Execution Timeline、Tool Calls、Blackboard viewer

---

## 四、文字合約 vs 架構示意圖 — 差異分析

| 面向 | 架構示意圖（完整願景） | 文字合約 v0（最小落地） | 差距 |
|------|----------------------|----------------------|------|
| 狀態機 | 階段式：Created → Planning → CollectingEvidence → Synthesizing → Critiquing → Completed + NeedsRevision / NeedsHumanReview | 簡單 6 態：Pending / Running / Succeeded / Failed / WaitingForFeedback / Cancelled | 圖是 run 的「階段」，合約是 run 的「狀態」，兩層未對齊 |
| MoE Router | 獨立組件，動態選擇 5 個專家中的子集 | 無 | 合約完全未提及 |
| DAG 結構 | 有並行分支（Research / Financial / Portfolio）→ 合流 | 線性四步（load → check → critique → finalize），明確說不支援 parallel | 並行能力在 v0 砍掉 |
| Critic Loop | 循環：CriticAgent → Pass / NeedsRevision / NeedsMoreResearch → 回到 DAG | 線性：critiqueAnswer → finalizeCriticReport 結束 | Loop 在 v0 砍掉，改為單向 |
| Blackboard artifacts | 6 種 artifact（ResearchFinding / FinancialMetricFinding / ...） | 一整塊 blackboard_json，内部結構自定義 | 合約用單一 jsonb 簡化 |
| 最終答案 | 結構化：Answer + Citations + Trace + 信心指標 | summary + findings + suggestedAnswerRevision | 信心指標在 v0 未實作 |
| Supervisor | 圖上暗示 LLM 決策 | 第一版 deterministic（純 DAG 依赖計算） | LLM supervisor 後續再做 |

---

## 五、待確認事項

1. 狀態機的兩層（run 狀態 vs run 階段）是否需要合併？還是 v0 只做 run 狀態、階段留給 v1？
2. MoE Router 在 v0 是否完全不碰？還是先做個簡單版（例如根據 query 關鍵字選 agent）？
3. 圖上的 Planner 節點在合約 DAG 中不存在，v0 要不要加？
4. 信心指標（Confidence）在 v0 最終答案中是否需要輸出？
5. 現有 chat 的 `research_stock_analysis` tool 跟 CriticReview 的 `getResearchRun` tool 是什麼關係？共用還是分開？

---

## 六、進度記錄

### Step 1: Entities + Migration [DONE]

**新增檔案：**
- `src/EquityLens.Api/Data/Entities/AgentRun.cs`
- `src/EquityLens.Api/Data/Entities/AgentRunNode.cs`
- `src/EquityLens.Api/Data/Entities/AgentRunEvent.cs`
- `src/EquityLens.Api/Data/Entities/AgentToolCall.cs`
- `src/EquityLens.Api/Data/Entities/AgentFeedback.cs`
- `src/EquityLens.Api/Data/Configurations/AgentRunConfiguration.cs`
- `src/EquityLens.Api/Data/Configurations/AgentRunNodeConfiguration.cs`
- `src/EquityLens.Api/Data/Configurations/AgentRunEventConfiguration.cs`
- `src/EquityLens.Api/Data/Configurations/AgentToolCallConfiguration.cs`
- `src/EquityLens.Api/Data/Configurations/AgentFeedbackConfiguration.cs`

**修改檔案：**
- `src/EquityLens.Api/Data/Entities/AppUser.cs` — 新增 `ICollection<AgentRun> AgentRuns`
- `src/EquityLens.Api/Data/EquityLensDbContext.cs` — 新增 5 個 DbSet

**Migration：**
- 名稱：`20260630013427_AddAgentRunTables`
- 已 apply 到本地 ParadeDB (equitylens)

**決策紀錄：**
- AgentRun → User 用 `Cascade`（删除 run 时一起删），不是 `SetNull`
- AgentRunNode/Event/ToolCall/Feedback → AgentRun 都是 `Cascade`
- AgentRunEvent/ToolCall/Feedback → AgentRunNode 都是 `SetNull`（node 可选）
- Status 默认值用字符串 `"Pending"` / `"Running"` / `"Requested"`，跟现有 JobRun / RiskRun 模式一致
- BlackboardJson 默认值 `"{}"` 在 C# 端设定，DB 层面是 NOT NULL jsonb

**注意事項：**
- 192 tests 全部通过
- DB 连接串用 Docker 内的 `equitylens/equitylens_dev_password`，不是 `ymsh20220`

### Step 2: Service + Controller [DONE]

**新增檔案：**
- `src/EquityLens.Api/Contracts/AgentRun/AgentRunContracts.cs` — DTOs
- `src/EquityLens.Api/Services/AgentRuns/IAgentRunService.cs` — Interface
- `src/EquityLens.Api/Services/AgentRuns/AgentRunService.cs` — Implementation
- `src/EquityLens.Api/Services/AgentRuns/CriticReviewWorkflow.cs` — Workflow 定義 + Blackboard helpers
- `src/EquityLens.Api/Controllers/AgentRunsController.cs` — 5 個 API endpoints

**修改檔案：**
- `src/EquityLens.Api/Program.cs` — 加 `IAgentRunService` / `AgentRunService` DI 註冊

**API Endpoints：**
```
POST   /api/agent-runs/critic-review     — 建立 CriticReview run
GET    /api/agent-runs?limit=&workflowType=&status= — 列出 runs
GET    /api/agent-runs/{runId}           — run detail（含 nodes/events/toolCalls）
POST   /api/agent-runs/{runId}/retry     — retry failed run
POST   /api/agent-runs/{runId}/cancel    — 取消 run
```

**決策紀錄：**
- Controller 繼承 `ApiControllerBase`，用 `ToActionResult(Result<T>)` 統一錯誤處理
- Service 用 `ICurrentUserContext` 取得登入使用者 ID
- Retry 只允許 `Failed` → `Pending`，會清掉 nodes/events/toolCalls 並重置 blackboard
- Cancel 只允許非終態（`Succeeded`/`Failed`/`Cancelled` 不能取消）
- CriticReviewWorkflow 是 static class，定義 DAG definition 和 initial blackboard

**注意事項：**
- 192 tests 全部通過
- Workflow runner（Step 3）尚未實作，目前只是 CRUD 層

### Step 3: CriticReview Runner [DONE]

**新增檔案：**
- `src/EquityLens.Api/Services/AgentRuns/IWorkflowNode.cs` — Node interface + context + result
- `src/EquityLens.Api/Services/AgentRuns/AgentWorkflowRunner.cs` — Deterministic DAG executor
- `src/EquityLens.Api/Services/AgentRuns/LoadResearchRunNode.cs` — Load research run into blackboard
- `src/EquityLens.Api/Services/AgentRuns/CheckEvidenceNode.cs` — Check citation coverage & evidence quality
- `src/EquityLens.Api/Services/AgentRuns/CritiqueAnswerNode.cs` — LLM-based critique (DeepSeek/Gemini)
- `src/EquityLens.Api/Services/AgentRuns/FinalizeCriticReportNode.cs` — Final report generation

**修改檔案：**
- `src/EquityLens.Api/Program.cs` — DI 註冊 AgentWorkflowRunner + 4 個 IWorkflowNode
- `src/EquityLens.Api/Services/AgentRuns/AgentRunService.cs` — 注入 runner，建立 run 後背景執行

**架構說明：**
- `AgentWorkflowRunner` 用 Kahn's algorithm 做 topological sort，依 DAG 依賴順序執行 node
- 每個 node 執行前後都寫 `agent_run_event`（NodeStarted/NodeCompleted/NodeFailed）
- Node 失敗時 run 標記為 Failed，停止後續 node
- `CheckEvidenceNode` 是純規則式（rule-based），不呼叫 LLM
- `CritiqueAnswerNode` 呼叫 `IChatCompletionService`，解析 LLM 回傳的 JSON findings
- `FinalizeCriticReportNode` 計算 overallSeverity、生成 summary 和 suggestedRevision
- CreateCriticReview API 回傳 Pending 後，背景 `Task.Run` 立即啟動 workflow

**決策紀錄：**
- Workflow execution 用 `Task.Run` 而不是 Hangfire/background service，保持 v0 簡單
- Node registration 用 `IWorkflowNode` interface + DI，方便後續新增 node 類型
- CheckEvidence 用簡單 heuristic（數字 vs 引用），不做複雜 NLP
- CritiqueAnswer 的 LLM prompt 全中文，跟現有 codebase 一致

**注意事項：**
- 192 tests 全部通過
- LoadResearchRunNode 假設 researchRunId 指向一個已存在的 AgentRun
