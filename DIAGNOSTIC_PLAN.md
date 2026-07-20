# Workflow 診斷能力現狀與改進計畫

> 目標：Workflow出錯能查到原因。

---

## 1. 現狀：診斷資料存在哪裡

| 資料 | 存放位置 | 能查到什麼 |
|---|---|---|
| Node 執行結果 | `AgentRunNode.InputJson` / `OutputJson` | 每個 Node 讀了什麼、產出什麼 |
| Node 錯誤 | `AgentRunNode.ErrorMessage` | 失敗原因（string） |
| Node 耗時 | `AgentRunNode.DurationMs` | 每個 Node 執行多久 |
| 執行事件 | `AgentRunEvent` (`BlackboardUpdated`, `ToolCallStarted`, ...) | 時間線：誰在什麼時候做了什麼 |
| LLM/工具調用 | `AgentToolCall.ArgumentsJson` / `ResultJson` / `ErrorMessage` | LLM 呼叫的輸入、輸出、錯誤 |
| Blackboard 最終狀態 | `AgentRun.BlackboardJson` | **只有最終版，中間版本不保存** |
| Run 整體狀態 | `AgentRun.Status` / `ErrorMessage` / `OutputJson` | 整體成功或失敗 |

**查詢入口**：`GET /api/agent-runs/{runId}`，返回 `AgentRunDetailResponse`。

---

## 2. 缺口：出事時查不到什麼

| # | 缺口 | 影響 | 嚴重度 |
|---|---|---|---|
| G1 | **Blackboard 中間版本不保存** | 出錯時不知道 blackboard 在哪一步被誰改了什麼 | 高 |
| G2 | **錯誤沒有結構化分類** | `ErrorMessage` 是 string，無法按類型聚合統計 | 高 |
| G3 | **Node 沒有宣告 producedKeys** | 無法知道某個 Node 改了 blackboard 的哪些 key | 中 |
| G4 | **沒有成本/Token 預算** | 無法知道一個 run 花了多少錢、是否超支 | 中 |
| G5 | **Node 沒有記錄 Blackboard 版本** | 無法追溯某個 Node 讀了哪個版本、寫了哪個版本 | 中 |
| G6 | **沒有 Golden Dataset** | 無法做回歸測試，無法知道問題是否重複發生 | 低 |
| G7 | **嵌套物件變更偵測不精確** | `DetectChangedKeys()` 只能偵測頂層 key 變化，無法追蹤嵌套屬性的原地修改（如 `CriticReview` 上加 routing 欄位） | 中 |
| G8 | **無變更重序列化造成偽陽性** | 部分 handler 只讀不寫但會重序列化 blackboard，序列化順序差異可能被誤判為變更 | 低 |
| G9 | **陣列累積無法區分新增** | `RetrievalHistory` 等陣列每次迭代 append，偵測到變化但無法知道「這次新增了什麼」vs「之前的元素還在」 | 低 |

---

## 3. 改進計畫（按優先順序）

### Phase 1：結構化錯誤（G2）

**目標**：讓錯誤可分類、可聚合、可搜尋。

#### 3.1 新增結構化錯誤 Exception

```csharp
// 新檔案：AgentNodeException.cs
public sealed class AgentNodeException : Exception
{
    public string ErrorCode { get; }
    public string ErrorCategory { get; }
    public bool Retryable { get; }

    public AgentNodeException(
        string errorCode,
        string errorCategory,
        string message,
        bool retryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ErrorCategory = errorCategory;
        Retryable = retryable;
    }
}
```

#### 3.2 錯誤分類常數

```csharp
// 新檔案：AgentNodeErrorCategories.cs
public static class AgentNodeErrorCategories
{
    public const string TransientFailure = "TransientFailure";
    public const string PermanentFailure = "PermanentFailure";
    public const string ValidationFailure = "ValidationFailure";
    public const string PolicyRejection = "PolicyRejection";
    public const string TimedOut = "TimedOut";
    public const string Cancelled = "Cancelled";
    public const string InsufficientEvidence = "InsufficientEvidence";
}
```

#### 3.3 AgentRunNode 加欄位

```csharp
// AgentRunNode.cs 加
public string? ErrorCode { get; set; }
public string? ErrorCategory { get; set; }
public bool? ErrorRetryable { get; set; }
```

#### 3.4 修改 Handler 錯誤記錄方式

現有 handler 的 `throw new InvalidOperationException("...")` 改為：

```csharp
// 舊
throw new InvalidOperationException("Research run not found.");

// 新
throw new AgentNodeException(
    "research_run_not_found",
    AgentNodeErrorCategories.PermanentFailure,
    "Research run not found.",
    retryable: false);
```

#### 3.5 修改 Executor 捕獲邏輯

`AgentRunExecutor.RunNodeAsync` 的 catch 區塊：

```csharp
catch (AgentNodeException ex)
{
    node.ErrorCode = ex.ErrorCode;
    node.ErrorCategory = ex.ErrorCategory;
    node.ErrorRetryable = ex.Retryable;
}
catch (OperationCanceledException)
{
    node.ErrorCode = "timeout";
    node.ErrorCategory = AgentNodeErrorCategories.TimedOut;
    node.ErrorRetryable = false;
}
```

#### 3.6 API Response 加欄位

`AgentRunNodeResponse` 加：`ErrorCode`, `ErrorCategory`, `ErrorRetryable`。

#### Phase 1 改動範圍

| 檔案 | 改動 |
|---|---|
| 新增 `AgentNodeException.cs` | 自定義例外 |
| 新增 `AgentNodeErrorCategories.cs` | 錯誤分類常數 |
| `AgentRunNode.cs` | 加 3 個欄位 |
| `AgentRunExecutor.cs` | 修改 catch 區塊 |
| 所有 handler（28 處 throw） | 改用 `AgentNodeException` |
| EF Migration | 加 3 個欄位 |
| `AgentRunContracts.cs` | Response 加欄位 |
| 測試 | 新增錯誤分類測試 |

---

### Phase 2：Blackboard 版本追蹤（G1 + G3 + G5 + G7 + G8 + G9）

**目標**：知道 blackboard 在每一步的變化。

> **G1 盲區說明**：Phase 2 的 `DetectChangedKeys()` 只能告訴你「哪些 key 變了」，但**不保存每一步的完整 blackboard 快照**。例如 Node A 把 `CriticFindings` 從 `[]` 改成 `[finding1, finding2]`，你只知道「Node A 產生了 CriticFindings」，但不知道變化前後的具體值。如需完全追溯，見 Phase 2.5。

#### 3.7 Blackboard 加版本欄位

```csharp
// AgentBlackboardContracts - 每個 CreateInitial* 加
["schemaVersion"] = 1,
["blackboardVersion"] = 0
```

#### 3.8 AgentRunNode 加版本欄位

```csharp
public int? InputBlackboardVersion { get; set; }
public int? OutputBlackboardVersion { get; set; }
public string[]? ProducedBlackboardKeys { get; set; }
```

#### 3.9 AgentRunExecutor 記錄版本

```csharp
// RunNodeAsync 中
var boardBefore = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
node.InputBlackboardVersion = boardBefore["blackboardVersion"]?.GetValue<int>() ?? 0;

// handler.ExecuteAsync(...) 執行後
var boardAfter = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
node.OutputBlackboardVersion = boardAfter["blackboardVersion"]?.GetValue<int>() ?? 0;
node.ProducedBlackboardKeys = DetectChangedKeys(boardBefore, boardAfter);
```

#### 3.10 AgentNodeJson 加版本輔助

```csharp
public static string[] DetectChangedKeys(JsonObject before, JsonObject after)
{
    var changed = new List<string>();
    foreach (var kv in after)
    {
        if (before[kv.Key] is null || before[kv.Key]!.ToJsonString() != kv.Value!.ToJsonString())
            changed.Add(kv.Key);
    }
    return changed.ToArray();
}
```

#### Phase 2 改動範圍

| 檔案 | 改動 |
|---|---|
| `AgentBlackboardContracts.cs` | 6 個 `CreateInitial*` 加版本欄位 |
| `AgentRunNode.cs` | 加 3 個欄位 |
| `AgentNodeJson.cs` | 加 `DetectChangedKeys` |
| `AgentRunExecutor.cs` | 執行前後記錄版本 |
| EF Migration | 加 3 個欄位 |
| `AgentRunContracts.cs` | Response 加欄位 |
| 測試 | 版本追蹤單元測試 |

---

### Phase 2.5：可選 Blackboard 快照（G1 完全追溯）

**目標**：在關鍵 workflow 中保存每一步的完整 blackboard 快照，實現完全可追溯。

> **設計原則**：預設關閉（不影響效能），僅在需要深度診斷的 workflow 開啟。

#### 3.11 AgentRunNode 加快照欄位

```csharp
// AgentRunNode.cs 加
public string? BlackboardSnapshotJson { get; set; }  // jsonb, nullable
```

#### 3.12 AgentRun 加快照控制

```csharp
// AgentRun.cs 加
public bool EnableBlackboardSnapshots { get; set; }
```

#### 3.13 Executor 記錄快照

`AgentRunExecutor.RunNodeAsync` 中，handler 成功後：

```csharp
if (run.EnableBlackboardSnapshots)
{
    node.BlackboardSnapshotJson = run.BlackboardJson;  // handler 已寫入最新態
}
```

#### 3.14 Workflow 定義加控制項

```csharp
// AgentRun 上建立時設定
run.EnableBlackboardSnapshots = workflowType == "CriticReview" || workflowType == "EvidenceRemediation";
```

#### 3.15 API Response 加欄位

`AgentRunNodeResponse` 加：`BlackboardSnapshotJson`（nullable）。僅在 `EnableBlackboardSnapshots = true` 時有值。

#### Phase 2.5 改動範圍

| 檔案 | 改動 |
|---|---|
| `AgentRunNode.cs` | 加 1 個欄位 |
| `AgentRun.cs` | 加 1 個欄位 |
| `AgentRunExecutor.cs` | handler 成功後寫入快照 |
| EF Migration | 加 2 個欄位 |
| `AgentRunContracts.cs` | Response 加欄位 |
| 測試 | 快照開關測試 |

#### 儲存成本估算

| 情境 | Blackboard 大小 | Node 數 | 快照總量 |
|---|---|---|---|
| CriticReview | ~5 KB | 5 | ~25 KB |
| EvidenceRemediation | ~15 KB | 11 | ~165 KB |
| EvidenceReanalysis | ~20 KB | 7 | ~140 KB |

對資料庫影響可控，建議在 EvidenceRemediation 和 EvidenceReanalysis 上預設開啟。

---

### Phase 3：成本/Token 預算（G4）

**目標**：知道一個 run 花了多少錢，超出預算時降級。

#### 3.16 AgentRunNode 記錄成本

```csharp
public int? InputTokens { get; set; }
public int? OutputTokens { get; set; }
public decimal? EstimatedCostUsd { get; set; }
```

#### 3.17 AgentRun 記錄總成本

```csharp
public int TotalInputTokens { get; set; }
public int TotalOutputTokens { get; set; }
public decimal TotalEstimatedCostUsd { get; set; }
```

#### 3.18 Executor 累計成本

每次 Node 成功後，`RunNodeAsync` 累計到 `run`：

```csharp
run.TotalInputTokens += node.InputTokens ?? 0;
run.TotalOutputTokens += node.OutputTokens ?? 0;
run.TotalEstimatedCostUsd += node.EstimatedCostUsd ?? 0;
```

#### 3.19 LLM Agent 回傳成本資訊

現有 LLM agent 需統一回傳 `inputTokens`, `outputTokens`, `estimatedCostUsd`。

#### Phase 3 改動範圍

| 檔案 | 改動 |
|---|---|
| `AgentRunNode.cs` | 加 3 個欄位 |
| `AgentRun.cs` | 加 3 個欄位 |
| `AgentRunExecutor.cs` | 累計成本 |
| LLM agents | 統一回傳成本 |
| EF Migration | 加 6 個欄位 |
| `AgentRunContracts.cs` | Response 加欄位 |

---

## 4. 改動總覽

| Phase | 新增檔案 | 修改檔案 | Migration | 測試 |
|---|---|---|---|---|
| Phase 1 | `AgentNodeException.cs`, `AgentNodeErrorCategories.cs` | `AgentRunNode.cs`, `AgentRunExecutor.cs`, 28 個 handler, `AgentRunContracts.cs` | 1 | 錯誤分類測試 |
| Phase 2 | — | `AgentBlackboardContracts.cs`, `AgentRunNode.cs`, `AgentNodeJson.cs`, `AgentRunExecutor.cs`, `AgentRunContracts.cs` | 1 | 版本追蹤測試 |
| Phase 2.5 | — | `AgentRunNode.cs`, `AgentRun.cs`, `AgentRunExecutor.cs`, `AgentRunContracts.cs` | 1 | 快照開關測試 |
| Phase 3 | — | `AgentRunNode.cs`, `AgentRun.cs`, `AgentRunExecutor.cs`, LLM agents, `AgentRunContracts.cs` | 1 | 成本預算測試 |

---

## 5. 預期效果

改完後，`GET /api/agent-runs/{runId}` 返回：

```json
{
  "run": {
    "status": "Failed",
    "totalInputTokens": 12500,
    "totalOutputTokens": 3200,
    "totalEstimatedCostUsd": 0.08
  },
  "nodes": [
    {
      "nodeKey": "critiqueAnswer",
      "status": "Failed",
      "inputBlackboardVersion": 3,
      "outputBlackboardVersion": null,
      "producedBlackboardKeys": null,
      "errorCode": "llm_schema_invalid",
      "errorCategory": "ValidationFailure",
      "errorRetryable": true,
      "inputTokens": 4200,
      "outputTokens": 800,
      "estimatedCostUsd": 0.03,
      "errorMessage": "LLM response does not match schema."
    }
  ],
  "events": [
    { "eventType": "BlackboardUpdated", "message": "Evidence packet written." },
    { "eventType": "NodeFailed", "message": "Node critiqueAnswer failed." }
  ],
  "toolCalls": [
    { "toolName": "criticReviewLLM", "errorMessage": "...", "durationMs": 3200 }
  ],
  "blackboardJson": "..."
}
```

出事時能查到：
- 哪一步掛了（`nodes[].status = Failed`）
- 為什麼掛（`errorCode` + `errorCategory`）
- 能不能 retry（`errorRetryable`）
- 掛之前 blackboard 長什麼樣（`inputBlackboardVersion`）
- 掛之後 blackboard 有沒有被改（`outputBlackboardVersion`）
- 誰改了 blackboard 的哪些 key（`producedBlackboardKeys`）
- 花了多少錢（`totalEstimatedCostUsd`）
- 該步驟的完整 blackboard 快照（`blackboardSnapshotJson`，需開啟快照功能）
