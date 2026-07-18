# EquityLens Workflow Design Principles

> 本文件用來約束 AI 在 EquityLens 中設計、修改與審查 workflow。  
> 核心目標不是最大化 Agent 自主性，而是建立具備**正確性、可驗證性、可追蹤性、可恢復性與成本邊界**的研究流程。

---

## 1. 核心哲學

設計選擇的優先順序：

1. 正確性高於自主性。
2. 可驗證高於看起來聰明。
3. 可恢復高於一次成功。
4. 明確狀態高於隱性上下文。
5. 固定 DAG 高於過早動態規劃。
6. 結構化輸出高於自由文字。
7. 有界 loop 高於無限反思。
8. PostgreSQL 事實狀態高於 queue 暫態狀態。
9. 可重現性高於單次最佳答案。
10. 真實研究價值高於 Agent 數量。

---

## 2. Workflow 先於 Agent

設計 workflow 前必須先定義：

```text
名稱：
使用者目標：
主要輸入：
主要輸出：
完成條件：
不可接受的錯誤：
```

只有需要語義理解、非結構化資訊判斷或文字生成時，才使用 LLM。不得因為某一步「可以使用 Agent」，就預設它「應該使用 Agent」。

### Deterministic First

以下工作應優先由 deterministic code 執行：

- 金融數學計算
- schema 與欄位驗證
- 數值、日期、期間、單位與幣別比對
- graph validation 與 state transition
- retry、timeout、權限與冪等性判斷
- citation 是否存在
- 重複資料檢查

以下工作較適合 LLM：

- 意圖與實體消歧
- 非結構化證據摘要
- claim extraction
- claim 與 citation 的語義支持判斷
- 相反證據辨識
- 研究缺口描述
- 自然語言答案生成與修訂

LLM 不得取代可形式化的計算或驗證。

---

## 3. Constrained Agentic Workflow

EquityLens 採用受約束的 Agentic Workflow：

- NodeType 必須來自註冊白名單。
- Node 輸入與輸出必須有 schema。
- Graph 必須在執行前驗證。
- Loop、retry、tool call、token 與執行時間必須有上限。
- 不允許 Agent 自行發明未註冊 NodeType。
- 不允許無限檢索、無限反思或無限改寫。
- Workflow route 必須由結構化 decision 決定，不得解析模糊自然語言來控制流程。

第一版優先採固定 deterministic DAG。只有真實需求出現後，才加入條件分支、bounded loop、動態規劃、平行節點或多 Agent 協作。

---

## 4. Node 設計原則

每個 Node 只能負責一項清楚職責。若無法用一句話描述其責任，通常代表範圍過大。

好的名稱：

```text
ResolveSecurity
PlanRetrieval
RetrieveDocuments
ExtractClaims
ValidateQuantitativeClaims
GenerateFinalAnswer
```

不好的名稱：

```text
AnalyzeEverything
ResearchAndValidateAndWrite
SmartFinanceAgent
```

每個 Node 必須定義：

```text
NodeKey
NodeType
Stage
Purpose
RequiredInputs
ProducedOutputs
Preconditions
SuccessCriteria
FailureModes
RetryPolicy
Timeout
SideEffects
IdempotencyKey
```

範例：

```json
{
  "nodeKey": "validateQuantitativeClaims",
  "nodeType": "ValidateQuantitativeClaims",
  "stage": "Validation",
  "requiredBlackboardKeys": ["claims", "evidencePacket"],
  "producedBlackboardKeys": ["quantitativeValidationResults"],
  "timeoutSeconds": 30,
  "retryPolicy": { "maxAttempts": 1 },
  "sideEffects": false
}
```

Node 不得讀取未宣告的隱性狀態，也只能寫入宣告的 produced keys。

### Idempotency

所有可能重試或重複投遞的 Node，都必須定義：

- IdempotencyKey
- 是否已完成
- 重複執行的行為
- 是否允許覆寫

不得假設 queue 具備 exactly-once delivery。

---

## 5. Failure Is Data

失敗不能只保存 exception message。至少應包含：

```json
{
  "errorCode": "llm_schema_invalid",
  "errorCategory": "TransientFailure",
  "message": "LLM response does not match schema.",
  "retryable": true,
  "nodeKey": "extractClaims",
  "attempt": 2
}
```

錯誤類型至少區分：

- `TransientFailure`
- `PermanentFailure`
- `ValidationFailure`
- `PolicyRejection`
- `TimedOut`
- `Cancelled`
- `InsufficientEvidence`

`InsufficientEvidence` 通常是 workflow branch condition，不是系統 exception。

Retry 應設定在 Node 層級，並依錯誤類型、副作用與冪等性決定是否重試。不得所有失敗都重跑整條 workflow。

---

## 6. Blackboard 原則

Blackboard 是受 schema 約束的共享狀態，不是 JSON 垃圾桶。

建議依語義分區：

```json
{
  "input": {},
  "research": {},
  "evidence": {},
  "claims": {},
  "critique": {},
  "revision": {},
  "runtime": {}
}
```

Blackboard 必須包含：

```text
schemaVersion
blackboardVersion
```

每次 Node 更新應記錄：

```text
inputBlackboardVersion
outputBlackboardVersion
producedKeys
```

大型文件、完整 provider response 或大型中間結果應存為 artifact，Blackboard 只保存引用與必要摘要。

執行軌跡至少保留 Node input hash、Node output、修改 keys、workflow version、prompt version、model configuration、token、成本與執行時間。

---

## 7. Graph 與控制流程

執行前至少驗證：

- NodeKey 唯一
- NodeType 已註冊
- Edge 指向存在的 Node
- 無未允許的 cycle
- required inputs 有來源
- 必要 Node 可從起點到達
- 終點可到達
- 分支有預設處理
- Loop 有最大次數

分支必須基於結構化決策：

```json
{
  "requiresMoreEvidence": true,
  "missingEvidence": [
    {
      "claimId": "claim-004",
      "requiredEvidenceType": "earnings_release",
      "period": "2026Q2",
      "reasonCode": "citation_period_mismatch"
    }
  ]
}
```

任何 retrieval、critic 或 revision loop 都必須定義：

```text
maxRounds
maxToolCalls
maxDocuments
maxTokens
maxDuration
exitConditions
```

不得使用「直到答案足夠好」作為唯一終止條件。

---

## 8. Stage 設計原則

### 8.1 Stage 的定位

每個 Node 應指定一個 `Stage`，用來描述它在 workflow 中的語義區段。

> **Edge 決定執行順序，Stage 決定 Node 的語義分組。**

Stage 不得取代 graph dependency、state machine 或 execution order。

不得假設：

- 同一 Stage 的 Node 可以任意排序
- Stage 只能出現一次
- Stage 必須線性遞增
- 完成一個 Stage 後不能再次回到該 Stage
- Runtime 可以依 Stage 名稱推導執行順序

例如補充檢索流程可能合法地出現：

```text
Evidence
→ Validation
→ Retrieval
→ Evidence
→ Validation
```

因此 Stage 是 metadata，不是第二套 workflow engine。

### 8.2 Stage 的用途

Stage 可用於：

- UI 分區與進度顯示
- latency、token、成本與失敗率統計
- stage-level timeout 與 budget policy
- worker routing 或資源配置
- telemetry attributes
- workflow review 與除錯
- 說明 Node 在整體研究流程中的角色

Stage 不應直接用於：

- 決定 Node execution order
- 隱式建立 dependency
- 取代 Node status
- 取代 graph edge
- 將同 Stage Node 強制視為可平行執行

### 8.3 建議 Stage 清單

第一版使用固定且精簡的 Stage 集合：

```text
Input
Planning
Retrieval
Evidence
Analysis
Validation
Decision
Generation
Finalization
```

實際 workflow 不必使用全部 Stage。

不得為每個 Node 建立專屬 Stage，否則 Stage 會退化成 NodeType 的別名。新增 Stage 時必須說明現有 Stage 為何不足、新 Stage 的語義邊界，以及對 UI、metrics 或 policy 的實際價值。

### 8.4 Stage 與 NodeType

```text
Stage = workflow 中的語義區段
NodeType = 實際執行行為與 handler 類型
```

例如：

```json
{
  "nodeKey": "validateQuantitativeClaims",
  "nodeType": "ValidateQuantitativeClaims",
  "stage": "Validation"
}
```

```json
{
  "nodeKey": "validateCitationSupport",
  "nodeType": "ValidateCitationSupport",
  "stage": "Validation"
}
```

兩個 Node 可屬於同一 Stage，但必須保有各自獨立的 contract、handler 與 dependency。

### 8.5 Stage Metadata

每個 workflow node definition 至少應包含：

```json
{
  "id": "checkEvidence",
  "type": "CheckEvidence",
  "stage": "Validation",
  "required": true
}
```

Node metadata 建議包含：

```csharp
public sealed record NodeMetadata(
    string NodeType,
    string Stage,
    IReadOnlyList<string> RequiredBlackboardKeys,
    IReadOnlyList<string> ProducedBlackboardKeys,
    bool HasSideEffects = false,
    bool IsIdempotent = true);
```

建立 Workflow Run 時，建議將 Stage snapshot 寫入 `AgentRunNode`，確保歷史紀錄不受未來 workflow definition 變更影響。

`stageOrder` 可供 UI 排序，但不得控制執行順序。

### 8.6 Stage Status

第一版不建立獨立 Stage entity 或持久化 Stage state。Stage status 應由其 Node status 動態聚合，例如：

```text
任一 Node Running → Stage Running
任一 required Node Failed → Stage Failed
所有 required Node Succeeded 或 Skipped → Stage Succeeded
部分成功且部分 Skipped → Stage PartiallySucceeded
```

避免同時維護 Run、Stage、Node 三套可能互相矛盾的狀態來源。若未來需要持久化 Stage 狀態，必須先證明動態聚合無法滿足查詢或效能需求。

### 8.7 Stage-Level Policy

Stage 可提供共用預設 policy，但 Node-level policy 必須可以覆寫。

```json
{
  "stage": "Retrieval",
  "maxDurationSeconds": 60,
  "maxToolCalls": 10
}
```

```json
{
  "stage": "Analysis",
  "maxInputTokens": 20000,
  "maxOutputTokens": 4000
}
```

Policy precedence：

```text
Node override
→ Stage default
→ Workflow default
→ System default
```

Validation Stage 失敗時不得默認繼續 Publish；是否允許 fallback 必須由明確 policy 決定。

### 8.8 Stage Observability

每個 Node span 與 metric 至少加入：

```text
workflow.type
workflow.version
node.key
node.type
node.stage
```

建議 metrics：

```text
agent_node_duration_ms{stage="Validation"}
agent_node_failures_total{stage="Retrieval"}
llm_input_tokens_total{stage="Analysis"}
llm_output_tokens_total{stage="Generation"}
workflow_stage_duration_ms{stage="Evidence"}
```

Stage duration 必須由實際 Node timeline 聚合，不得假設 Stage 是單一連續時間區段。

---

## 9. LLM Node 原則

LLM Node 必須優先使用 structured output，schema 應具備：

- 有限明確欄位
- enum 狀態
- 必填欄位
- 數值範圍
- 禁止任意新增欄位
- schema version

每次 LLM call 至少記錄：

```text
promptTemplateId
promptVersion
model
provider
temperature
maxOutputTokens
responseSchemaVersion
inputTokens
outputTokens
estimatedCost
```

Prompt 變更視同程式碼變更，必須版本控制與測試。

不得信任 LLM 自述「已驗證」。數字、citation ID、source span、計算結果與 route legality 都必須由 deterministic validator 再確認。

每個 LLM Node 只接收完成任務所需的最小 context，不得預設傳入整個 Blackboard、全部文件、所有事件與完整對話。

---

## 10. 金融領域規則

以下計算必須由金融計算模組執行：

- Return、XIRR、TWR
- Volatility
- Covariance、Correlation、Beta
- VaR、ES、Component Risk
- Monte Carlo 與 Stress Testing
- Kupiec 與 Christoffersen tests

LLM 只能解釋結果，不得成為正式數值來源。

重要數值至少附帶：

```text
value
unit
currency
period
asOfDate
calculationMethod
source
confidence
```

輸出必須區分：

- `ReportedFact`
- `CalculatedMetric`
- `ModelEstimate`
- `ManagementGuidance`
- `AnalystInference`
- `LLMInterpretation`

不得將估算值包裝成已發生事實。

### Citation-to-Claim

每個重要 claim 應連結一個或多個 citation。驗證狀態至少包含：

- `Supported`
- `PartiallySupported`
- `Unsupported`
- `Contradicted`
- `Unverifiable`

數值 claim 優先驗證數字、正負號、單位、幣別、期間、YoY/QoQ、actual/guidance 與 GAAP/non-GAAP。

---

## 11. Queue 與 Worker 可靠性

Redis Queue 負責傳遞工作；PostgreSQL 是 run 狀態、ownership、attempt 與完成狀態的事實來源。

Worker 執行前必須 atomic claim，不得採用：

```text
SELECT Pending
→ application 判斷
→ UPDATE Running
```

長時間工作應保存：

```text
workerId
claimedAtUtc
heartbeatAtUtc
leaseExpiresAtUtc
attemptCount
rowVersion
```

Worker 異常終止後，系統應能重新認領 lease 過期的工作。

超過最大嘗試次數後進入 dead-letter 狀態，至少保存：

```text
runId
nodeKey
attempts
lastErrorCode
lastErrorMessage
failedAtUtc
workflowVersion
```

不得無限重試。

---

## 12. Observability

每個 Workflow Run 至少可查詢：

- workflow type 與 version
- current status 與 current node
- node timeline
- 每個 Node 的 input/output
- retry、tool calls、model 與 prompt version
- token、成本、latency、error code 與 trace ID
- Stage 聚合進度、耗時、成本與錯誤率

每個 Node 至少產生：

```text
NodeReady
NodeStarted
NodeSucceeded
NodeFailed
NodeRetried
NodeSkipped
```

---

## 13. Testing 與 Evaluation

測試至少涵蓋：

1. 純計算 unit tests
2. Node handler tests
3. Graph validation tests
4. State transition tests
5. Queue/worker integration tests
6. Workflow end-to-end tests
7. LLM evaluation tests
8. Stage aggregation 與 policy precedence tests

LLM output 不以完整字串相等作為唯一判斷，應驗證 schema、必要事實、citation grounding、unsupported claim rate 與 contradiction detection。

研究 workflow 應建立 golden dataset，包含：

- 正常完整證據
- 證據不足
- 引用錯誤
- 數值矛盾
- 季度錯置
- 多來源衝突
- 文件無法取得
- LLM schema invalid

修改 prompt、model、retrieval、graph 或 Stage policy 後，必須執行 regression evaluation。

---

## 14. 成本、安全與權限

每條 workflow 必須定義：

```text
maxDuration
maxLLMCalls
maxToolCalls
maxInputTokens
maxOutputTokens
maxDocuments
maxEstimatedCost
```

超出預算時應降級、縮小範圍、跳過低優先步驟或回傳明確標示的部分結果，不得靜默超支。

Tool call 前必須驗證：

- 使用者資料存取權
- portfolio/security/document ownership
- Node 是否允許使用該 tool
- 參數 schema
- 敏感資料與不可逆副作用

LLM 不得自行擴大權限或繞過 application service。

---

## 15. AI 設計 Workflow 的強制輸出格式

AI 提出 workflow 設計時，必須依序輸出：

### A. Workflow 目的

```text
名稱：
使用者目標：
主要輸入：
主要輸出：
完成條件：
```

### B. Node 清單

| NodeKey | NodeType | Stage | 類型 | 主要責任 | 輸入 | 輸出 |
|---|---|---|---|---|---|---|

類型只能是：`Deterministic`、`LLM`、`Tool`、`HumanApproval`、`ControlFlow`。

### C. Graph

列出所有節點、條件分支與 loop 上限。必須明確說明 Edge，而不是以 Stage 暗示順序。

### D. Failure Policy

對每個 Node 說明 timeout、retryable/permanent errors、fallback 與 dead-letter 行為。

### E. State Contract

列出 required/produced Blackboard keys、artifacts 與 schema versions。

### F. Stage Contract

列出：

- 每個 Node 的 Stage
- 使用的 Stage 清單
- Stage-level default policy
- Node override 規則
- Stage status 聚合方式
- Stage observability 指標

### G. Observability

列出 events、metrics、trace attributes、token 與成本資料。

### H. Tests

至少提出正常、邊界、失敗、retry、duplicate delivery、Stage aggregation 與 LLM schema invalid 案例。

### I. 風險與不做事項

明確說明限制、不應由 LLM 執行的部分、第一版暫不實作功能與未來擴充條件。

---

## 16. Review Checklist

AI 完成設計後必須自查：

- [ ] 任務與完成條件明確
- [ ] 每個 Node 單一責任
- [ ] 可形式化工作由 deterministic code 執行
- [ ] Node input/output 有 schema
- [ ] NodeType 已註冊
- [ ] 每個 Node 已指定固定清單中的 Stage
- [ ] Stage 只表達語義分組，不控制 execution order
- [ ] Graph 可驗證
- [ ] 未假設同 Stage Node 可任意排序或平行
- [ ] Stage 未過度細分成 NodeType 別名
- [ ] Stage status 由 Node status 聚合
- [ ] Stage-level policy 有明確 default 與 override 規則
- [ ] 歷史 Run 保存 Stage snapshot
- [ ] Telemetry 記錄 `node.stage`
- [ ] Loop 與 retry 有上限
- [ ] 已考慮 duplicate delivery 與 idempotency
- [ ] Blackboard 只保存必要狀態
- [ ] 大型資料存為 artifact
- [ ] 重要 claim 可追溯 citation
- [ ] 數值附帶期間、單位與口徑
- [ ] 區分事實、計算、估算與觀點
- [ ] 有 timeout、cancel、lease 與 dead-letter
- [ ] 記錄 workflow、prompt 與 schema version
- [ ] 有 token、成本與 latency 邊界
- [ ] 可從 Node checkpoint 恢復
- [ ] 有 golden dataset 或可重現測試
- [ ] 未將 prototype 宣稱為 production-ready

---

## 17. 最終原則

EquityLens workflow 的成熟度不以 Agent 數量衡量，而取決於：

- 每個數字能說明如何算出
- 每個結論能追溯至證據
- 每個 Node 有明確責任
- 每個 Stage 有清楚語義但不干涉 Graph
- 每次失敗能辨識原因
- 每個工作能安全重試
- 每個版本能被重現
- 每個成本能被衡量
- 每個自動決策都有邊界

> **讓 LLM 負責理解，讓程式負責約束；讓 workflow 負責把兩者組合成可驗證的結果。**
