# Research Agent Runtime v4 — 設計與實作規格

> **一句話定位**：A Durable, Observable, Policy-Constrained Agent Runtime。
> LLM 對研究過程有充分自主權；工具權限、資料驗證、審批與結果提交由 Runtime 以確定性程式保證。

| 項目 | 內容 |
|---|---|
| 狀態 | Proposed → 依本文件 Phase 0–5 實作 |
| 適用範圍 | `ResearchInvestigation` 研究流程（新增 `ResearchAgent` workflow 並行，不刪除 v1） |
| 不在範圍 | `PortfolioDiagnosis`、`FeedbackRevision`、`EvidenceRemediation`、`EvidenceReanalysis` 的遷移（見 §13 Roadmap） |
| 讀者 | 實作 agent（夜間自動執行）、Reviewer、面試官 |
| 執行進度紀錄 | [`research-agent-runtime-progress.md`](research-agent-runtime-progress.md)（實作者必須持續更新） |

---

## 任務簡報（必須遵守）

> **閱讀方式**：本節與 §0 是**約束**；§1 之後是**建議做法**。
> 實作時以「目標、邊界、驗收清單」為準。§1 之後的類別名稱、欄位、檔案配置都可以依實際程式碼調整，甚至換成更簡單的做法；只要驗收項目都有測試證明、邊界沒有被違反即可。偏離建議時，在 §15 記錄「原建議 / 實際做法 / 理由」。

### 目標

`ResearchInvestigation` 新增 **AgentLoop 引擎**：用固定的粗粒度 workflow，加上一個在確定性邊界內自主選工具的 LLM 迴圈，取代「LLM 動態生成 DAG」。預設引擎維持 v1，行為不變；兩者以 eval A/B 決定去留。同時修補 runtime 現有的可靠性漏洞。

### 邊界（不可違反）

1. 不修改、不 commit `src/EquityLens.Api/appsettings.json`；不新增真實外部副作用工具（下單、寄信、改投組）。
2. 預設設定下 v1 行為不變；不跳過、停用、刪除既有測試。
3. 新 workflow **不得**呼叫 graph planner（`LlmAgentWorkflowPlanner` / `DynamicPlanValidator` / `GraphMaterializer`）。
4. Source policy 由**結構**保證：`LocalOnly` 時 Web 檢索不可能被執行（工具不可見，且呼叫時也會被拒絕），不能只靠 prompt。
5. 數字不經 LLM 搬運：計算類工具不接受 LLM 直接傳入的數值資料。
6. 狀態變更只能透過狀態機（`AgentStateMachine` 既有或新增的明確方法）。
7. 本地文件搜尋沿用 `search_document_chunks(...)`（經由既有 `IDocumentRetriever`）。
8. 新 node type 必須同時註冊在 DI 與 `AgentWorkflowCatalog`。
9. 遵守 `AGENTS.md`；不在 commit、程式碼、文件中寫入模型識別名稱。

### 驗收清單（每一項都要有自動化測試證明）

| # | 驗收條件 | 優先 |
|---|---|---|
| A1 | Worker 認領到「租約被他人持有」的 run 時**不 ack**，訊息不遺失，之後仍會被處理 | MUST |
| A2 | 節點執行中途 worker 當掉（節點停在 `Running`、租約過期），run 能被新 worker 接手並完成，**不會**因為「沒有 ready 節點」而失敗 | MUST |
| A3 | 長時間節點會續約租約；已失去租約的 worker **無法**寫入節點結果（fencing） | MUST |
| A4 | `Engine = AgentLoop` 時建立新 workflow，且整個 run 期間 graph planner 未被呼叫；未指定時仍走 v1 | MUST |
| A5 | 迴圈中 LLM 可自行決定工具、參數、輪數與起草時機；同一輪多個工具呼叫**平行**執行，且每個呼叫都有紀錄 | MUST |
| A6 | 不可見工具、違反政策、參數不合法、超出預算的呼叫都被拒絕，**拒絕本身也被記錄**，並回饋給 LLM | MUST |
| A7 | 迴圈在第 N 輪後中斷，恢復時從第 N+1 輪繼續，**已成功的工具呼叫不重做** | MUST |
| A8 | 預算耗盡、無進展、LLM 回傳格式連續錯誤時，迴圈有明確停止原因並優雅結束（有證據就起草，沒有就標準拒答） | MUST |
| A9 | 輸出與 v1 相容：既有 Critic 節點與 eval harness **不需修改**即可處理 AgentLoop 的結果（answer、citations、candidates、ResearchRun） | MUST |
| A10 | 答案有懸空引用或違反 source policy 時會被確定性關卡攔下，進入一次修訂；修訂後仍不合格則改為標準拒答 | MUST |
| A11 | 每一輪 LLM 決策可查詢：模型、prompt 版本、動作、精簡理由、token、成本、延遲，以及對應的工具呼叫 | MUST |
| A12 | eval harness 可用 `--engine` 指定引擎，並輸出軌跡指標（至少：web 政策違規、planner fallback 率、平均輪數 / 工具呼叫數）；舊的 `responses.jsonl` 仍可 rescore | SHOULD |
| A13 | 副作用工具（以**測試專用**工具驗證）執行前必須審批；審批綁定工具與參數，參數改變即失效；核准後只執行一次（冪等）；Reject 必須附理由 | SHOULD |
| A14 | OTel：迴圈輪次、LLM 呼叫、工具執行有 span；工具呼叫、Guard 拒絕、租約遺失、節點中斷有 metrics | SHOULD |
| A15 | 品質關卡可依風險觸發人工審閱（沿用既有 `WaitForHumanApproval` 機制） | COULD |
| A16 | 實際跑 v1 vs AgentLoop 的 A/B，報告存入 `docs/eval-verification/`，依 §10.3 門檻給出結論 | COULD |
| A17 | 全部既有測試通過；`dotnet build` 成功；`has-pending-model-changes` 無待處理變更；`evals` 的 `pytest` 全綠 | **每次 commit 都要成立** |

### 完成順序

A1–A3 → A4–A11 → A12 → A13 → A14 → A15–A16。每完成一項就 commit + push，並更新 progress 檔；任何時間點分支都必須可建置、測試全綠。

---

## 0. 給實作 Agent 的執行指引（先讀這一節）

本節是工作方式與專案地雷的約束；§1 之後是建議做法。驗收以上方「任務簡報」為準。

### 0.1 開工前必讀（依序）

1. `AGENTS.md`（repo 根目錄，專案規則，**優先於本文件**以外的任何慣例）
2. 本文件全文
3. `docs/architecture/agent-platform-contract.md`、`docs/architecture/WORKFLOW_DESIGN_PRINCIPLES.md`
4. 程式：`src/EquityLens.Api/Services/Agents/` 下的 `AgentRunExecutor.cs`、`AgentNodeRunner.cs`、`AgentRunWorker.cs`、`AgentStateMachine.cs`、`AgentWorkflowCatalog.cs`、`ResearchInvestigationWorkflow.cs`、`AgentApprovalService.cs`、`HumanApprovalNodeHandlers.cs`、`Bootstrap/AgentRuntimeRegistration.cs`
5. 測試範例：`tests/EquityLens.Api.Tests/Services/Agents/AgentExecutorBoundaryTests.cs`、`AgentExecutorTestFactory.cs`、`AgentRunWorkerTests.cs`、`HumanApprovalWorkflowTests.cs`
6. Eval harness：`evals/README.md`、`evals/equitylens_eval/client.py`、`scorers.py`

### 0.2 硬性規則（違反任何一條都視為失敗）

- **不要修改或 commit** `src/EquityLens.Api/appsettings.json`（內含本機 API key）。新設定放 `appsettings.Development.json` 以外的程式預設值或 Options 類別預設值。
- **不要刪除或改壞 v1**：現有 `ResearchInvestigation`（Dynamic DAG）必須在預設設定下行為不變，現有測試全部維持綠燈。
- **不要**跳過、停用、刪除任何既有測試來讓建置變綠。
- **不要**加入 Moq/NSubstitute；沿用手寫 fake 的慣例。
- **不要**新增任何會產生真實外部副作用的工具（下單、寄信、改投組）。副作用審批機制只用測試專用工具驗證。
- 狀態變更**一律**透過 `AgentStateMachine` 的 `Transition(...)` / `ResetForRetry(...)` 或本文件新增的明確方法，不可直接 `Status = ...`。
- 新的 node type **必須**同時註冊在 `Bootstrap/AgentRuntimeRegistration.cs` 與 `AgentWorkflowCatalog.cs`（`GetNode` 用 `Single()`，漏註冊會在執行時爆）。
- 控制器/服務的 public XML doc comment 用**繁體中文**。
- 本地文件搜尋**必須**沿用 DB function `search_document_chunks(...)`（透過現有 `IDocumentRetriever`），不可改寫成 inline LINQ。
- Unit test 使用 EF InMemory：需要單元測試覆蓋的路徑**不要**使用 `ExecuteUpdateAsync` / raw SQL（InMemory 不支援）；用 tracked entity + concurrency token。
- 不要在 commit message、程式註解或文件中寫入任何模型識別名稱。

### 0.3 工作方式

- 分支：從包含本文件的分支切出 `feat/research-agent-runtime`，所有工作在此分支進行並 push。**不要 push 到 `main`。**
- **每完成一個 Phase 就 commit + push 一次**，commit message 格式：`feat(agent-runtime): phase N - <摘要>`。
- 每個 Phase 結束時執行驗證指令（§0.4），並把結果、測試數量、遇到的問題寫進 `research-agent-runtime-progress.md`。
- **Phase 有優先序**（§12）。時間不夠時，寧可完整做完較少的 Phase，也不要每個 Phase 都做一半。
- 遇到本文件與現有程式碼衝突：**以現有程式碼的實際行為為準**，在 progress 檔的「設計偏差」區記錄你做的調整與理由，然後繼續。不要停下來等人。
- 遇到真正無法解決的阻礙（例如缺少外部服務）：記錄在 progress 檔的「阻塞」區，跳到下一個不依賴它的工作項目。

### 0.4 驗證指令

```bash
# 每個 Phase 結束都要跑
dotnet build src/EquityLens.Api/EquityLens.Api.csproj
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj

# 只跑 agent 相關（開發中快速回饋）
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj --filter FullyQualifiedName~Services.Agents

# 有新增/修改 EF entity 時
~/.dotnet/tools/dotnet-ef migrations has-pending-model-changes --project src/EquityLens.Api --startup-project src/EquityLens.Api

# Eval harness（Python，離線測試）
cd evals && pip install -e '.[test]' && pytest
```

`NU1510 System.Text.Encoding.CodePages` 警告是既有的，可忽略。

---

> ### 📌 以下章節（§1–§14）為建議做法
> 內容是依閱讀現有程式碼推導出的設計，**未經編譯驗證**。類別名稱、欄位、資料表、檔案位置都是建議；若實際程式碼有更簡單或更正確的做法，請採用，並在 §15 記錄偏差。各章節列出的測試名稱是建議的驗證方式，可以改名或合併，但對應的驗收項目（A1–A17）必須都有測試覆蓋。

## 1. 背景與動機

### 1.1 現況（v1：Dynamic DAG）

`ResearchInvestigation` 建立時只有 `Validate → DetectIntent` 兩個 bootstrap 節點，其餘由 LLM planner 在執行中動態「長出」：

```text
Validate → DetectIntent
            └─ ResearchContextReady → 🧠 LlmAgentWorkflowPlanner → DynamicPlanValidator → GraphMaterializer
                 PlanRetrieval → RetrieveLocal → EvaluateEvidence
                                                   └─ CapabilityRequestsReady → 🧠 planner（第 2 次）
                 [RetrieveWeb] → Rank → Draft → BuildEvidencePacket → CheckEvidence → Critique → FinalizeCriticReport
                                                                                         └─ CriticCompleted → 🧠 planner（第 3 次）
```

LLM 決策粒度是「圖」：決定下一段接哪些 node type；每次決策經過 skill catalog → capability registry → LLM 提案 → validator → 失敗退回 `DeterministicDynamicWorkflowPlanner` → materializer。

### 1.2 問題

| # | 問題 | 證據 |
|---|---|---|
| P1 | **架構負擔重**：每個研究步驟都要 node type + handler + catalog contract + capability + DI，再加 planner/validator/materializer 三層 | `Services/Agents` 約 10k 行；`AgentDynamicPlanCoordinator` 的 trigger 判斷為多層巢狀三元運算 |
| P2 | **LLM 空間其實很小**：只能從預定義 capability 挑節點，不能決定查詢內容、輪數、組合方式 | `LlmAgentWorkflowPlanner` system prompt |
| P3 | **planner 價值未經驗證**：沒有 LLM planner vs 確定性 planner 的對照數據 | `evals/` 無此比較 |
| P4 | **單一 run 內無平行**：executor 一次只取一個 ready node；`Services/Agents`、`Services/Ai/Retrieval` 內無 `Task.WhenAll` | `AgentRunExecutor.GetNextReadyNode` |
| P5 | **租約 + ack 縫隙**：executor 遇到有效租約時 `return`，worker 仍 `Acknowledge`；租約 5 分鐘、`PendingMinIdleSeconds` 預設 300 秒，worker 當機後的重新認領可能剛好落在租約到期前，訊息被 ack 掉，run 永久卡在 `Running` | `AgentRunExecutor.cs`（lease check）、`AgentRunWorker.ProcessNextAsync` |
| P6 | **節點執行中斷 = run 失敗**：node 在 `Running` 狀態時 worker 當機，之後 `GetNextReadyNode` 只挑 `Pending`，走到 `"Workflow has no ready node but is not complete."` 例外，整個 run 變 `Failed` | `AgentRunExecutor.ExecuteAsync` 尾段 |
| P7 | **長節點超過租約**：30 秒批次限制只在節點之間檢查，單一長節點可超過 5 分鐘租約；無 heartbeat 續約、無寫入 fencing | `AgentRunExecutor`、`AgentNodeRunner` |
| P8 | **HITL 只有固定節點**：`WaitForHumanApproval` 與節點層級審批存在，但無法對「某一次具體工具呼叫」審批 | `AgentApprovalRequest` 綁 `AgentRunNodeId`，無 tool call 欄位 |
| P9 | **Eval 未使用軌跡**：harness 輪詢 `GET /api/agent-runs/{id}` 但只取 `["run"]`，nodes/events/toolCalls 全部丟棄 | `evals/equitylens_eval/client.py::_investigate` |

### 1.3 目標

| ID | 目標 | 衡量方式 |
|---|---|---|
| G1 | LLM 對研究過程有自主權：查什麼、查幾輪、何時起草 | 新 workflow 不經 graph planner |
| G2 | 安全邊界不依賴 LLM 自律 | Source policy、參數、預算、副作用皆由 Runtime 強制；有測試證明 |
| G3 | 可觀測：每個 LLM 決策、工具呼叫、Guard 拒絕、checkpoint 皆有結構化紀錄 | 新資料表 + 事件 + OTel span |
| G4 | 可恢復：迴圈可在任一輪中斷後從 checkpoint 續跑；可暫停等人數天不佔 worker | 故障注入測試 |
| G5 | HITL：副作用前強制審批，審批綁定具體工具呼叫與參數 | 測試 |
| G6 | 以 eval 決定去留：v1 與 v4 A/B 比較 | harness `compare` 報告 |

### 1.4 非目標

- 不改用 provider 原生 function calling（現有 `IChatCompletionService` 只有 `SystemPrompt/UserPrompt` + `ChatResponseFormat.JsonObject`，見 §6.2）。
- 不讓 LLM 選擇檢索演算法（BM25 / Vector / Hybrid）；`Hybrid` + rerank 是經 eval 調校的預設，維持在工具內部。
- 不在本期移除 v1 Dynamic DAG 程式碼（等 A/B 結果，見 §13）。
- 不實作真實副作用工具（下單、寄信、改投組）。

---

## 2. 設計原則

1. **控制結果與邊界，不控制路線。** 從「事前審查 LLM 規劃的圖」改為「劃定工具權限與預算，事後驗收產出」。
2. **依失敗後果分配控制權。** 失敗只多花錢 → 交給 LLM；失敗會產生錯誤數字、越權或副作用 → Runtime 決定。
3. **政策用結構執行，不用 prompt 拜託。** `LocalOnly` 時 `search_web` 不出現在工具清單，且 dispatcher 收到也會拒絕（雙層）。
4. **數字不經 LLM 搬運。** 計算類工具只接受設定參數；數值資料由工具從 Blackboard / DB 讀取。
5. **一套 orchestrator。** 新 workflow 不經 `AgentDynamicPlanCoordinator`；不要把 tool loop 疊在 graph planner 之上。
6. **持久化是交易邊界。** 每一輪迴圈結束都是一個 durable commit point；中斷後從最後一個 commit point 續跑。
7. **HITL 依例外觸發。** 唯讀與計算自動執行；副作用、超預算、品質不達標才請人。

---

## 3. 目標架構總覽

```text
┌──────────────────── Durable Runtime（確定性，既有 + 加固）────────────────────┐
│ Redis Stream ─► AgentRunWorker ─► AgentRunExecutor ─► AgentNodeRunner          │
│   租約 + heartbeat 續約 + fencing │ outbox │ 狀態機 │ 中斷節點接手              │
└────────────────────────────────────┬──────────────────────────────────────────┘
                                     ▼
 ResearchAgent workflow（固定、粗粒度 DAG）

 ┌───────────┐  ┌──────────────┐  ╔═══════════════════════════════════════╗
 │ Validate  │─►│ DetectIntent │─►║ ResearchAgentLoop (Mode=Research)      ║
 │ 授權/政策 │  │              │  ║  🧠 LLM：Observe → Decide → Act ↻      ║
 └───────────┘  └──────────────┘  ║  🛡 ToolGuard  ⚡ 平行 dispatch         ║
                                  ║  💾 每輪 checkpoint  ⏸ 副作用前審批     ║
                                  ╚══════════════════╤════════════════════╝
                                                     ▼
                                  ┌────────────────────────────────────────┐
                                  │ OutputGate（確定性）                    │
                                  │ 引用存在 / 數字可追溯 / 政策 / schema    │
                                  └──────────────────┬─────────────────────┘
                                                     ▼
              BuildEvidencePacket → CheckEvidence → CritiqueAnswer → FinalizeCriticReport   （沿用 v1 節點）
                                                     ▼
                                  ┌────────────────────────────────────────┐
                                  │ RouteAfterQualityGate（確定性）          │
                                  │ 寫入 qualityRoute.decision              │
                                  └──────────────────┬─────────────────────┘
              condition qualityRoute.decision == "Revise"      │ （"Accept" → 下列兩節點 Skipped）
                  ╔══════════════════════════════╗  ┌──────────────────────┐
                  ║ ResearchAgentLoop (Revise)   ║─►│ OutputGate (Revise)  │
                  ╚══════════════════════════════╝  └──────────┬───────────┘
              condition qualityRoute.decision == "HumanReview" │
                  ┌──────────────────────────────┐             │
                  │ WaitForHumanApproval（結果）  │◄────────────┘ （可選，Phase 4）
                  └──────────────────────────────┘
                                                     ▼
                                     run Succeeded → CompleteRun 寫回 ResearchRun（既有邏輯）
```

外框只負責生命週期：**授權 → 自主研究 → 驗收 → 品質關卡 → （修訂）→ （人工）→ 提交**。

---

## 4. 決策權分配

| 能力 | 決策者 | 執行點 | 理由 |
|---|---|---|---|
| 查哪些文件、關鍵字、年度、頁面範圍 | LLM | `search_local` 參數 | 語意判斷 |
| 檢索演算法、rerank | Runtime | `search_local` 工具內部（`IDocumentRetriever` + `IResultReranker`） | 已 eval 調校；避免無收益的變異 |
| 查幾輪、何時起草 | LLM（預算內） | Loop | 研究彈性 |
| 是否上網 | LLM 提出 → Runtime 依政策允許 | ToolGuard | `sourcePolicy` 不可被繞過 |
| 計算方法（風險、成長率） | LLM 選工具與設定參數 | `compute_*` 工具 | 計算由確定性程式執行 |
| 計算用的數值資料 | Runtime | 工具內讀 DB/Blackboard | 數字不經 LLM |
| 工具參數是否合法 | Runtime | ToolGuard（JSON schema + 業務規則） | — |
| 預算（輪數、Web 次數、token、成本、時間） | Runtime | LoopBudget | — |
| 是否宣告「證據足夠」 | LLM 提議 `submit_answer` → Runtime 驗收 | OutputGate + Critic | 避免自行宣布合格 |
| 副作用工具是否執行 | Policy + 人 | ToolApproval | 安全邊界 |
| 是否提交最終答案 | Runtime | OutputGate、RouteAfterQualityGate | — |
| Retry / checkpoint / recovery / lease | Runtime | Executor、Loop | 不交給 LLM |

---

## 5. Phase 1：Runtime 加固（P5、P6、P7）

> 這一 Phase 與新架構無關，任何路線都需要。**優先序最高。**

### 5.1 Worker ack 修正（P5）

**變更**

```csharp
// IAgentRunExecutor.cs
public enum AgentRunExecutionOutcome { Executed, SkippedTerminalOrWaiting, LeaseHeldByOther, LeaseLostDuringExecution }
public interface IAgentRunExecutor
{
    Task<AgentRunExecutionOutcome> ExecuteAsync(Guid runId, Guid userId, CancellationToken cancellationToken = default);
}
```

- `AgentRunExecutor`：租約被他人持有時回傳 `LeaseHeldByOther`；DB concurrency conflict 搶租約失敗也回 `LeaseHeldByOther`；terminal / waiting 狀態回 `SkippedTerminalOrWaiting`。
- `AgentRunWorker.ProcessNextAsync`：`LeaseHeldByOther` → **不 ack**（訊息留在 pending list，之後由 `ReadStalePendingAsync` 再認領）；其餘 → ack。
- `AgentRunQueueOptions.PendingMinIdleSeconds` 預設改為 **420**（> 租約 300 秒），並在 Options 類別加 XML doc 說明兩者關係。
- 更新所有實作 `IAgentRunExecutor` 的測試 fake。

**驗收測試**（`AgentRunWorkerTests.cs`）
- `ProcessNextAsync_WhenLeaseHeldByOther_DoesNotAcknowledge`
- `ProcessNextAsync_WhenRunTerminal_Acknowledges`
- 既有 5 個 worker 測試維持通過。

### 5.2 租約 heartbeat 與 fencing（P7）

新增 `IAgentRunLeaseManager`（`Services/Agents/AgentRunLeaseManager.cs`）：

```csharp
public interface IAgentRunLeaseManager
{
    /// <summary>嘗試延長租約；若持有者已不是自己則回傳 false（fenced）。</summary>
    Task<bool> RenewAsync(AgentRun run, string expectedOwner, CancellationToken ct);
    /// <summary>寫入前檢查仍為租約持有者，否則丟出 AgentRunLeaseLostException。</summary>
    void EnsureOwned(AgentRun run, string expectedOwner);
}
```

- 新增欄位 `AgentRun.LeaseVersion`（`long`，concurrency token）作為 **fencing token**；每次取得租約 `LeaseVersion++`。需要 EF migration（命名 `AddAgentRunLeaseVersion`）。
- Executor 取得租約後把 `leaseOwner` 放進 `AgentNodeExecutionContext.LeaseOwner`。
- `RenewAsync`：重新讀取 `LeaseOwner`/`LeaseVersion`（用 `Entry(run).ReloadAsync` 或比對 DB 值），符合才把 `LeaseExpiresAtUtc` 延長到 now + 5 分鐘並 `SaveChanges`；`DbUpdateConcurrencyException` → 回傳 false。
- 呼叫點：`AgentNodeRunner` 執行 handler 時以背景 `PeriodicTimer`（每 60 秒）續約；`ResearchAgentLoop` 每一輪 commit 前呼叫 `EnsureOwned`。
- 續約失敗（fenced）→ 取消 handler 的 linked CancellationToken，executor 回傳 `LeaseLostDuringExecution`，**不得**再寫入節點結果。
- 使用 `TimeProvider`（.NET 內建）注入時間，`AgentRunExecutor`、`AgentNodeRunner`、`AgentRunLeaseManager`、Loop 新程式碼中**不要**直接用 `DateTime.UtcNow`。DI 註冊 `TimeProvider.System`；測試用 `Microsoft.Extensions.TimeProvider.Testing` 的 `FakeTimeProvider`（若新增套件不便，可自寫簡單 fake）。既有程式碼中的 `DateTime.UtcNow` 只在本 Phase 觸及的方法內替換，不做全域重構。

**驗收測試**（新檔 `AgentRunLeaseTests.cs`）
- `Renew_WhenStillOwner_ExtendsExpiry`
- `Renew_WhenAnotherWorkerTookLease_ReturnsFalse`
- `LongRunningNode_HeartbeatKeepsLeaseAlive`（FakeTimeProvider 快轉 > 5 分鐘）
- `FencedWorker_CannotCommitNodeResult`

### 5.3 中斷節點接手（P6）

在 `AgentRunExecutor` 取得租約之後、批次迴圈之前，加入 `RecoverInterruptedNodes(run)`：

- 條件：run 為 `Running`，且存在節點狀態為 `Ready` / `Queued` / `Running`（代表上一個持有者在執行中失去租約）。
- 處理：
  - 寫事件 `NodeInterrupted`（payload：previousStatus、previousLeaseOwner、attempt）。
  - 若 handler 實作 `IResumableAgentNodeHandler`（§6.6）且存在 checkpoint → 透過新的狀態機方法 `RecoverInterrupted(node)` 回到 `Pending`，保留 checkpoint，下一次執行從 checkpoint 續跑。
  - 否則 → `RecoverInterrupted(node)` 回到 `Pending` 並清除部分輸出（等同重試），`node.Iteration` 不變，累計 `InterruptionCount`；超過該節點 `MaxRetryCount` → 標記 `Failed`（`ErrorCategory = Interrupted`，新增於 `AgentNodeErrorCategories`）。
- 在 `AgentStateMachine` 新增 `RecoverInterrupted(AgentRunNode node)`：允許 `Ready|Queued|Running → Pending`，記錄 transition（沿用 `RecordNodeTransition` 與 metrics）。
- 新增欄位 `AgentRunNode.InterruptionCount`（int，預設 0），併入 5.2 的 migration。

**驗收測試**（`AgentExecutorBoundaryTests.cs` 增補）
- `InterruptedRunningNode_IsRecoveredAndRunCompletes`（模擬：節點標 `Running`、租約過期 → 再執行 → run `Succeeded`）
- `InterruptedNode_ExceedingRetryBudget_FailsWithInterruptedCategory`
- `InterruptedResumableNode_ResumesFromCheckpoint`（Phase 2 完成後補）

### 5.4 Phase 1 完成定義

- [ ] 5.1–5.3 程式與測試完成，`dotnet test` 全綠
- [ ] migration 建立且 `has-pending-model-changes` 無待處理變更
- [ ] `AGENTS.md` 的「V3 Agent Runtime」區塊補上：租約 heartbeat/fencing、中斷節點接手、worker 不 ack 的條件
- [ ] progress 檔更新

---

## 6. Phase 2：ResearchAgentLoop 核心

### 6.1 元件

| 元件 | 檔案（新增於 `Services/Agents/Loop/`） | 職責 |
|---|---|---|
| `AgentToolDefinition` | `AgentToolContracts.cs` | 工具描述：name、description（給 LLM）、`JsonObject ArgumentSchema`、`ToolRiskLevel`、`IdempotentRead`、`MaxCallsPerRun` |
| `IAgentTool` | `AgentToolContracts.cs` | `Definition` + `Task<AgentToolResult> InvokeAsync(AgentToolInvocation, CancellationToken)` |
| `IAgentToolRegistry` | `AgentToolRegistry.cs` | 由 DI 收集 `IEnumerable<IAgentTool>`；依 `ToolScope`（sourcePolicy、使用者、workflow、loop mode）回傳**可見工具清單** |
| `ToolGuard` | `ToolGuard.cs` | 每次呼叫前檢查：工具可見、schema 合法、業務規則、預算、風險等級 → `Allow` / `Deny(reason)` / `RequireApproval` |
| `LoopBudget` | `LoopBudget.cs` | 輪數、總工具呼叫、Web 次數、prompt+completion token、估計成本、wall-clock；`TryConsume` 由 Runtime 計數 |
| `ResearchAgentLoop` | `ResearchAgentLoop.cs` | 主迴圈：讀 checkpoint → 組 prompt → 呼叫 LLM → 解析 action → Guard → 平行 dispatch → 寫 turn/tool call/checkpoint → 停止判斷 |
| `EvidenceLedger` | `EvidenceLedger.cs` | 證據去重與編號（`E1`、`E2`…），完整內容存 Blackboard，LLM 只看摘要 |
| `ResearchAgentLoopNodeHandler` | `ResearchAgentLoopNodeHandlers.cs` | `IAgentNodeHandler` + `IResumableAgentNodeHandler`；包裝 Loop，負責輸入/輸出 Blackboard 契約 |

DI 全部在 `Bootstrap/AgentRuntimeRegistration.cs` 註冊（新增 `AddResearchAgentLoop(...)` 區段）。

### 6.2 LLM 行動協定（JSON，不用原生 function calling）

現有 `IChatCompletionService` 只支援單一 system + user prompt 與 `ChatResponseFormat.JsonObject`（DeepSeek JSON mode；Gemini 對 JsonObject 會丟 `NotSupportedException`，Loop 需捕捉並以 `StructuredLlmOutput` 既有方式處理或回報錯誤）。因此 Loop 採用**結構化行動協定**（與 `LlmConversationAgent` 同一風格）：

每輪 user prompt 由 Runtime 組出，包含：問題、ticker、sourcePolicy、可見工具清單（name/description/schema）、預算剩餘、Evidence Ledger 摘要、前幾輪的精簡歷史（每輪：thoughtSummary + 工具名稱 + 結果摘要）、Guard 拒絕紀錄、（Revise 模式）Critic findings 與人工意見。

LLM 必須回傳：

```json
{
  "thoughtSummary": "繁體中文，≤ 200 字，說明本輪判斷",
  "action": "call_tools | submit_answer | ask_human | give_up",
  "toolCalls": [
    { "callId": "c1", "tool": "search_local", "arguments": { "query": "營業毛利", "documentType": "AnnualReport", "year": 2025 } }
  ],
  "answer": {
    "markdown": "台積電 2025 年毛利率約 59.9% [E3]。",
    "citedEvidenceIds": ["E3"],
    "claims": [{ "text": "2025 年毛利率約 59.9%", "evidenceIds": ["E3"], "numbers": ["59.9%"] }]
  },
  "question": "（ask_human 時）要問使用者的問題"
}
```

- 解析失敗或 schema 不符 → 記錄為該輪 `ParseError`，回饋錯誤訊息給下一輪，計入預算；連續 2 次解析失敗 → 停止並以 `StopReason = ProtocolFailure` 結束（交給 OutputGate，可能產生「資料不足」）。
- `give_up` → 產生標準拒答（沿用 `"目前提供的資料不足以回答此問題。"` 文字，`InsufficientEvidence` 狀態）。
- Prompt 範本 ID `research-agent-loop`，版本常數 `PromptVersion = 1`；若 `PromptManagementService` / `AgentPromptSnapshots` 可直接接入就接入，否則用常數並在 progress 檔記錄。
- System prompt 規則（至少）：只能使用清單內工具；數字只能引用 Evidence Ledger 或計算工具結果；每個含數字的句子必須引用 `[E#]`；`thoughtSummary` 與答案用繁體中文；保留原始公司名稱與 ticker；不得自行編造來源。

### 6.3 工具（第一批）

| 工具 | 風險等級 | 底層 | 參數（LLM 可控） | Runtime 控制 |
|---|---|---|---|---|
| `search_local` | `ReadOnly` | `IDocumentRetriever`（`search_document_chunks`）+ `IResultReranker` | `query`、`documentType?`、`year?`、`topK?(≤10)` | ticker 固定為 run 的 ticker；檢索模式依 `RetrievalOptions`；`WebOnly` 時不可見 |
| `search_web` | `ExternalRead`（計 Web 預算） | `IWebRetriever` | `query`、`recencyDays?` | `LocalOnly` 時不可見；`LocalThenWeb` 時第 1 輪不可見（需先有一次 `search_local`）；`MaxCallsPerRun = 1` |
| `get_document_page` | `ReadOnly` | 既有文件 chunk 查詢 | `documentId`、`pageNumber` | 只能讀已出現在 Ledger 的 documentId |
| `compute_growth` | `Compute` | 新的確定性函式 | `metricEvidenceIds[]`、`kind: yoy|qoq|cagr` | 數值從 Ledger 中的結構化數字讀取；LLM 不可直接傳數字 |

- 檢索結果寫入 Ledger 時保留 v1 所需欄位：chunk id、document id、title、documentType、pageNumber、url、quoteText、rankBeforeRerank、rankAfterRerank、relevanceScore、sourceType。
- 第一批**不要**把 `PortfolioRiskMath` 接成工具（屬於 `PortfolioDiagnosis` 遷移，見 §13）。
- **測試專用副作用工具** `test_publish_note`（`ToolRiskLevel.SideEffect`）只在測試 DI 註冊，用來驗證 §8 審批流程。

`ToolRiskLevel`：`ReadOnly` | `ExternalRead` | `Compute` | `SideEffect`。與 catalog 既有 `SideEffectLevel` 字串的對應寫在 `AgentToolContracts.cs` 的 XML doc。

### 6.4 迴圈演算法

```text
ResearchAgentLoop.RunAsync(context, mode):
  state ← LoadCheckpoint(node) ?? InitialState(board, mode)
  tools ← registry.Visible(scope(run, board, mode, state))
  while true:
    leaseManager.EnsureOwned(run, context.LeaseOwner)
    if budget.Exhausted(state)          → stop(BudgetExhausted)
    if state.NoProgressTurns ≥ 2        → stop(NoProgress)
    turnId ← NewGuid
    llm ← chat.CompleteAsync(BuildPrompt(state, tools), JsonObject)     // 記錄 AgentLlmTurn
    action ← Parse(llm)                                                  // 失敗 → ParseError 路徑
    switch action.Kind:
      submit_answer: state.ProposedAnswer ← action.Answer; stop(AnswerSubmitted)
      give_up:       stop(AgentGaveUp)
      ask_human:     → §8.3（Phase 4，未實作前視為 give_up 並記錄）
      call_tools:
        decisions ← action.ToolCalls.Select(ToolGuard.Evaluate)          // Allow / Deny / RequireApproval
        if any RequireApproval → 寫 pending tool call + checkpoint → context.RequestApproval(toolCall) → return Paused
        results ← Task.WhenAll(allowed.Select(Dispatch))                 // ⚡ 同輪平行；每個呼叫各自 timeout
        ledger.Merge(results); state.NoProgressTurns ← newEvidence == 0 ? +1 : 0
    state.Turn++
    CommitTurn(turnRecord, toolCalls, guardDecisions, checkpoint)        // 💾 單一 SaveChanges = durable commit point
  WriteOutputs(board, state)   // §6.7
```

- **平行**：同一輪的 allowed tool calls 以 `Task.WhenAll` 執行；每個呼叫獨立 timeout（預設 30 秒）與例外處理，單一工具失敗不影響同輪其他工具，失敗結果回饋給下一輪。
- **注意 DbContext 非執行緒安全**：工具若需要 DB，必須用 `IServiceScopeFactory` 建立各自 scope / DbContext，或在平行段之前/之後循序存取；`AgentToolCall` 紀錄在平行段結束後於主執行緒統一寫入。
- **預設預算**（`ResearchAgentLoopOptions`，Options pattern，section `AgentRuntime:ResearchAgentLoop`）：`MaxTurns = 6`、`MaxToolCalls = 16`、`MaxToolCallsPerTurn = 4`、`MaxWebCalls = 1`、`MaxPromptTokens = 60000`（累計）、`MaxCostUsd = 0.10`、`MaxWallClockSeconds = 240`；Revise 模式 `MaxTurns = 3`。
- **停止原因**（`ResearchAgentStopReasons`）：`AnswerSubmitted`、`BudgetExhausted`、`NoProgress`、`AgentGaveUp`、`ProtocolFailure`、`LeaseLost`、`Cancelled`。
- `BudgetExhausted` / `NoProgress` 時若 Ledger 有證據 → 再做**一次**「強制起草」LLM 呼叫（只允許 `submit_answer` / `give_up`），避免白跑；否則拒答。

### 6.5 Context 管理

- Ledger 完整內容存 Blackboard key `researchAgentLedger`（新 key，加入 `AgentBlackboardKeys`）。
- Prompt 中每筆證據只放：`id`、documentType、title、page、≤ 280 字 snippet；Ledger 超過 24 筆時只放與問題最相關的 24 筆（以 rerank 分數排序），其餘只列 id + title。
- 歷史輪次只放 thoughtSummary、工具名稱、結果摘要（例：「search_local → 5 筆，新增 E6–E10」），**不放原始檢索全文**。

### 6.6 可恢復性（Checkpoint）

新增介面：

```csharp
public interface IResumableAgentNodeHandler : IAgentNodeHandler
{
    bool HasCheckpoint(AgentNodeExecutionContext context);
}
```

新增 entity `AgentLoopCheckpoint`（`Data/Entities/AgentLoopCheckpoint.cs` + Configuration，表名 `agent_loop_checkpoint`）：

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | uuid PK | |
| AgentRunId / AgentRunNodeId | uuid FK | |
| Turn | int | 已完成的輪數 |
| StateJson | jsonb | 精簡歷史、提議答案、停止旗標、NoProgressTurns |
| LedgerVersion | int | 對應 Blackboard 中 Ledger 版本 |
| BudgetJson | jsonb | 已消耗預算 |
| PendingToolCallId | uuid? | 等待審批中的工具呼叫 |
| LeaseVersion | long | 寫入時的 fencing token |
| CreatedAtUtc | timestamptz | |

- 每輪結束的 `CommitTurn` 在**同一次** `SaveChanges` 寫入：`AgentLlmTurn`、該輪所有 `AgentToolCall`、Guard 事件、Ledger（Blackboard）、新的 `AgentLoopCheckpoint`（保留最新 N=3 筆即可）。
- 節點恢復（§5.3 或審批後重跑）時：讀最新 checkpoint → 還原 state 與預算 → 從 `Turn + 1` 繼續。**已成功的工具呼叫不重做。**
- `AgentNodeRunner` 目前在 handler 成功後才寫 `ProducedBlackboardKeys` / blackboard version：Loop 在中途 commit 時仍要透過現有 `AgentNodeJson` 工具函式維持 `blackboardVersion` 遞增的一致性；節點最終完成時由 runner 照常收尾。

### 6.7 輸出契約（與 v1 相容，讓既有 Critic 節點與 eval harness 不用改）

`ResearchAgentLoop`（Research 模式）完成時**必須**寫出與 v1 `RankAndSelectResearchEvidence` + `DraftResearchAnswer` 相同的 Blackboard keys 與 DB 紀錄：

- Blackboard：`selectedEvidence`、`answer`、`citations`、`candidates`、`researchRun`（結構對照 `DraftResearchAnswerNodeHandler` 實際寫入內容）。
- DB：`ResearchRun.Answer/Status/Model/...` 與 `ResearchRunCitations`（同 v1 的寫入方式；citation 的 `[n]` 由 `[E#]` 依出現順序轉換為 `1..n`）。
- 候選清單 `candidates` 要包含所有進入 Ledger 的證據與 `decision`（`Selected` = 被引用、`Rejected` = 未引用），讓 harness 的 `retrieval_recall` 與 `source_hit` 照常運作。
- 另外寫入新 key `researchAgent`：`{ turns, stopReason, toolCallCount, webCalls, guardDenials, ledgerSize, mode }`。

Revise 模式完成時寫 `revisedAnswer`（`CompleteRun` 既有邏輯會優先採用 `revisedAnswer`）並更新 `ResearchRunCitations`。

### 6.8 Phase 2 驗收測試（新檔 `ResearchAgentLoopTests.cs`，以 scripted fake chat 驅動）

- `Loop_SubmitsAnswer_AfterTwoTurns_WritesV1CompatibleKeys`
- `Loop_ParallelToolCalls_AllRecordedAsToolCalls`（同輪 3 個呼叫，3 筆 `AgentToolCall`，同一 `LlmTurnId`）
- `Loop_LocalOnly_SearchWebNotVisible_AndDispatcherDeniesIfRequested`
- `Loop_LocalThenWeb_WebHiddenUntilLocalSearchDone`
- `Loop_WebBudget_SecondWebCallDenied`
- `Loop_InvalidArguments_DeniedWithReason_AndFedBack`
- `Loop_ComputeTool_RejectsRawNumbersFromLlm`
- `Loop_NoProgressTwice_StopsAndForcesDraft`
- `Loop_BudgetExhausted_StopsWithReason`
- `Loop_ProtocolFailureTwice_StopsGracefully`
- `Loop_CrashAfterTurn2_ResumesAtTurn3_WithoutRepeatingToolCalls`
- `Loop_LeaseLost_StopsWithoutCommitting`
- `Loop_GiveUp_ProducesInsufficientEvidenceAnswer`

---

## 7. Phase 3：Workflow 組裝、OutputGate 與品質路由

### 7.1 新 workflow：`ResearchAgent`

- `AgentWorkflowTypes.ResearchAgent = "ResearchAgent"`，`AgentTypes.Research`，`ResearchAgentWorkflow.Version = 1`。
- `ResearchAgentWorkflowDefinitionProvider`：建立 run 時**一次寫入完整固定 DAG**，`orchestrationMode = "Static"`（`AgentDynamicPlanCoordinator.TryAdvanceAsync` 對非 `DynamicStateful` 會直接 `return false`，請驗證此行為並加測試）。
- 加入 `AgentWorkflowCatalog.Workflows` 與所有新 node 的 `AgentNodeContract`。
- `CompleteRun` / `UpdateResearchArtifactTerminalAsync` / executor 中 `ResearchInvestigation or FeedbackRevision` 的判斷要納入 `ResearchAgent`（研究 artifact 狀態更新）。**順手**把這些判斷收斂成一個 helper（例如 `AgentWorkflowTypes.OwnsResearchArtifact(type)`），不要再新增一組 `is A or B or C`。

節點（id / type）：

| id | type | condition | 說明 |
|---|---|---|---|
| `validate` | `ValidateResearchRequest` | — | 沿用 v1 handler |
| `detectIntent` | `DetectResearchIntent` | — | 沿用 v1 handler |
| `researchAgent` | `ResearchAgentLoop` | — | Mode=Research；`plannedArguments.mode = "Research"` |
| `outputGate` | `ResearchOutputGate` | — | §7.2 |
| `buildEvidencePacket` | `BuildEvidencePacket` | — | 沿用 |
| `checkEvidence` | `CheckEvidence` | — | 沿用 |
| `critiqueAnswer` | `CritiqueAnswer` | — | 沿用 |
| `finalizeCriticReport` | `FinalizeCriticReport` | — | 沿用 |
| `routeQuality` | `RouteAfterQualityGate` | — | §7.3 |
| `reviseAgent` | `ResearchAgentLoop` | `qualityRoute.decision == "Revise"` | Mode=Revise |
| `reviseOutputGate` | `ResearchOutputGate` | `qualityRoute.decision == "Revise"` | 驗 `revisedAnswer` |
| `approveResearchAnswer` | `WaitForHumanApproval` | `qualityRoute.decision == "HumanReview"` | Phase 4；`HumanApprovalNodeHandlers` 的 catalog `AllowedNextNodeTypes` 需允許此用途 |

邊：`validate → detectIntent → researchAgent → outputGate → buildEvidencePacket → checkEvidence → critiqueAnswer → finalizeCriticReport → routeQuality → reviseAgent → reviseOutputGate → approveResearchAnswer`。
（executor 的語意：condition 不成立 → 節點 `Skipped`；`Skipped` 視為前置已滿足，因此 Accept 路徑會依序跳過其餘節點並完成 run。）

> 若 Phase 4 未完成，`approveResearchAnswer` 不要放進定義（`RouteAfterQualityGate` 不輸出 `HumanReview`）。

**實作前務必核對**：沿用的 Critic 節點的 `RequiredBlackboardKeys` 是否都由 `researchAgent` + `outputGate` 提供；不足就在 Loop 輸出補齊，**不要**修改 Critic handler 的行為。

### 7.2 `ResearchOutputGate`（確定性，`ReadOnly`）

檢查項目與結果：

| 檢查 | 規則 | 不通過 |
|---|---|---|
| 引用存在 | 答案中每個 `[n]` 都對應到一筆 citation；每個 `citedEvidenceIds` 都在 Ledger | `Blocking` |
| 數字可追溯 | 答案中含數字的句子必須帶引用；`claims[].numbers` 每個數字需出現在其引用證據的 quote 或計算工具結果中（比對前 NFKC 正規化、去千分位；單位換算容忍度 0.5%） | `Warning`（交給 Critic），比對完全失敗率 > 50% → `Blocking` |
| 政策 | `LocalOnly` 時 citation 不得有 Web 來源；`WebOnly` 時不得有本地來源 | `Blocking` |
| 禁用陳述 | 例如「保證獲利」 | `Blocking` |
| Schema | `answer` 非空、長度上限 | `Blocking` |

- 輸出 Blackboard key `outputGate`：`{ passed, blockingIssues[], warnings[], checkedAtTurn }`。
- 有 `Blocking` 時**不丟例外**：寫結果，交給 `RouteAfterQualityGate` 決定 Revise；Revise 後的 `reviseOutputGate` 仍 Blocking → 答案改為標準拒答 + 狀態 `CitationValidationFailed`（沿用 v1 狀態字串），run 仍 `Succeeded`（研究層面的失敗由 artifact status 表達，與 v1 一致）。
- 可重用 `Services/Ai/Retrieval/CitationValidator.cs` 的邏輯；數字抽取可參考 `evals/equitylens_eval/normalize.py` 的規則用 C# 實作（**不要**跨語言呼叫 Python）。

### 7.3 `RouteAfterQualityGate`（確定性）

寫入 `qualityRoute = { decision, reasons[] }`，`decision` 為字串：

1. `outputGate.passed == false` → `"Revise"`
2. `criticReview.recommendedNextAction == "AcceptAnswer"` 且 gate 通過 → `"Accept"`
3. 其餘 → `"Revise"`
4. （Phase 4）若 `request` 標記需要人工審閱，或 Critic 嚴重度為最高級 → `"HumanReview"`（優先於 Accept）

Revise 只允許一次（DAG 已展開成單一修訂分支，不需要額外計數）。

### 7.4 選擇引擎：設定 + 每次請求覆寫（為 A/B）

- `ResearchAskRequest` 新增**可選**欄位 `string? Engine = null`（`"DynamicDag"` | `"AgentLoop"`），放在參數列最後以維持既有呼叫相容。
- Options `AgentRuntime:ResearchEngine`，預設 **`DynamicDag`**（v1 行為不變）。
- `AgentRunService.CreateResearchInvestigationAsync`：`request.Engine ?? options.ResearchEngine` 決定建立 `ResearchInvestigation` 或 `ResearchAgent` run。API 回應 `ResearchInvestigationCreatedResponse.WorkflowType` 會反映實際 workflow。
- 一般使用者是否能指定 `Engine`：允許（研究結果品質相同等級的唯讀功能），但記錄在 run input。

### 7.5 Phase 3 驗收測試（新檔 `ResearchAgentWorkflowTests.cs`）

- `Definition_IsStatic_AndPlannerNeverInvoked`
- `Catalog_ContainsAllResearchAgentNodeTypes_AndHandlersRegistered`（反射/DI 檢查：每個 node type 都有 handler）
- `Planner_ExecutionOrder_MatchesExpectedTopology`
- `AcceptPath_SkipsReviseNodes_RunSucceeds_ArtifactAnswered`
- `RevisePath_WritesRevisedAnswer_ArtifactUsesRevisedAnswer`
- `OutputGate_BlocksDanglingCitation`
- `OutputGate_BlocksWebCitationUnderLocalOnly`
- `OutputGate_WarnsUntraceableNumber`
- `ReviseStillBlocked_FallsBackToInsufficientEvidence`
- `UserIsolation_OtherUserCannotReadRun`
- `EngineOverride_AgentLoop_CreatesResearchAgentRun`；`DefaultEngine_RemainsDynamicDag`
- 全部既有 `ResearchInvestigationWorkflowTests` 維持通過

### 7.6 Phase 3 完成定義

- [ ] 測試全綠；`AGENTS.md` 的 workflow 清單加入 `ResearchAgent` 流程圖文字版
- [ ] `docs/architecture/node-workflow.md` 若有 workflow 清單也同步更新
- [ ] progress 檔更新

---

## 8. Phase 4：Human-in-the-loop

### 8.1 三種審批

| 類型 | 觸發 | 機制 |
|---|---|---|
| A. 工具審批 | `ToolGuard` 判定 `RequireApproval`（`SideEffect` 工具、或超預算追加請求） | 迴圈內暫停（§8.2） |
| B. 澄清詢問 | LLM 回 `ask_human` | 迴圈內暫停，問題寫入 `AgentFeedback`（既有 entity）或 approval request 的 Reason |
| C. 結果審閱 | `RouteAfterQualityGate` 輸出 `HumanReview` | 既有 `WaitForHumanApproval` 節點 + 條件邊 |

**優先序**：A（必做，含測試工具）> C > B（可選）。

### 8.2 工具審批流程（A）

`AgentApprovalRequest` 新增欄位（同一個 migration `AddToolCallApprovals`）：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `Scope` | string，預設 `"Node"` | `"Node"` / `"ToolCall"` |
| `AgentToolCallId` | uuid? | 待執行的工具呼叫 |
| `ToolName` | string? | |
| `ArgumentsHash` | string? | SHA-256（正規化後 JSON）|
| `ArgumentsJson` | jsonb? | 供審批者閱讀 |
| `IdempotencyKey` | string? | `{runId}:{toolCallId}` |
| `LeaseVersionAtRequest` | long? | |

流程：

1. Guard 回 `RequireApproval` → 建立 `AgentToolCall`（`Status = "AwaitingApproval"`）+ `AgentApprovalRequest(Scope=ToolCall)` + checkpoint（`PendingToolCallId`）→ 呼叫 `context.RequestApproval()`（既有）→ 節點 `WaitingForFeedback`、租約釋放、**不寫 wake outbox**。
2. 審批者呼叫既有審批 API（`POST /api/agent-runs/{id}/approval`，`AgentApprovalService` / `AgentRunService.DecideApprovalAsync`），擴充 request 可帶 `approvalId`；Reject 必須附 comment（沿用既有規則與 `OrchestrationVersion` 併發保護）。
3. 恢復執行：Loop 從 checkpoint 讀到 `PendingToolCallId` →
   - **重新驗證**：工具仍可見、參數 hash 與審批時一致、預算仍足、審批未過期（預設 24 小時，Options 可調）、未被消費（`ConsumedAtUtc == null`）。
   - Approved → 以 `IdempotencyKey` 執行（`IAgentTool.InvokeAsync` 收到 key；測試工具需證明重複呼叫只生效一次）→ 標記 `ConsumedAtUtc` → 結果回饋下一輪。
   - Rejected → 工具呼叫標 `Rejected`，拒絕理由與 comment 回饋給 LLM 下一輪（LLM 可換方法或 give_up）。
4. 所有步驟寫事件：`ToolApprovalRequested`、`ToolApprovalDecided`、`ToolApprovalRevalidated`、`ToolApprovalConsumed`。

**驗收測試**（新檔 `ToolApprovalTests.cs`，使用測試工具 `test_publish_note`）
- `SideEffectTool_PausesRun_ReleasesLease_NoWakeOutbox`
- `Approve_ResumesFromCheckpoint_ExecutesExactlyOnce`
- `Approve_WithChangedArguments_IsRejectedByRevalidation`
- `Reject_RequiresComment_AndFeedsBackToAgent`
- `DuplicateResume_DoesNotDoubleExecute`（idempotency）
- `ExpiredApproval_IsNotConsumed`
- `ConcurrentDecisions_OnlyOneWins`

### 8.3 結果審閱（C）與澄清（B）

- C：`RouteAfterQualityGate` 的 `HumanReview` 條件、definition 加入 `approveResearchAnswer`；Rejected → run 照既有 PortfolioDiagnosis 模式處理（拒絕即 run Failed 並帶理由，或標記 artifact）— 實作前比對 `PortfolioDiagnosis` 的 approve/reject 分支並沿用同一模式。
- B（可選）：`ask_human` → 暫停；回答寫入 Blackboard `humanClarifications[]`，下一輪 prompt 帶入。

---

## 9. 可觀測性

### 9.1 新資料表 `AgentLlmTurn`（`agent_llm_turn`）

| 欄位 | 型別 |
|---|---|
| Id | uuid PK |
| AgentRunId / AgentRunNodeId | uuid FK |
| Turn | int |
| Mode | text（Research / Revise / ForcedDraft） |
| Provider / Model | text |
| PromptTemplateId / PromptVersion / RenderedPromptHash | text / int / text |
| Action | text（call_tools / submit_answer / ask_human / give_up / parse_error） |
| ThoughtSummary | text |
| RequestedToolCount / AllowedToolCount / DeniedToolCount | int |
| PromptTokens / CompletionTokens | int |
| EstimatedCostUsd | numeric |
| LatencyMs | bigint |
| FinishReason / ErrorMessage | text? |
| CreatedAtUtc | timestamptz |

`AgentToolCall` 新增 `AgentLlmTurnId`（uuid?）與 `ToolCallKey`（LLM 的 `callId`）、`GuardDecision`（text?）、`GuardReason`（text?）。被 Guard 拒絕的呼叫**也要**寫一筆 `AgentToolCall`（`Status = "Denied"`）。

`AgentRunDetailResponse` 新增 `IReadOnlyList<AgentLlmTurnResponse>? LlmTurns = null`（參數放最後，維持相容），`AgentRunService.GetByIdAsync` 一併查詢。`AgentToolCallResponse` 增加上述新欄位（放最後、可為 null）。

### 9.2 事件（`AgentEventTypes` 新增）

`AgentLoopStarted`、`AgentTurnCompleted`、`ToolGuardDenied`、`AgentLoopStopped`、`NodeInterrupted`、`LeaseRenewed`（只在失敗或每 5 次記一次，避免洗版）、`LeaseLost`、`OutputGateEvaluated`、`QualityRouteDecided`、`ToolApprovalRequested`、`ToolApprovalDecided`、`ToolApprovalRevalidated`、`ToolApprovalConsumed`。

### 9.3 OTel

在 `EquityLensTelemetry` 補：
- Span：`agent.loop.turn`（tag：turn、action、tool.count）、`gen_ai.chat`（`gen_ai.system`、`gen_ai.request.model`、`gen_ai.usage.input_tokens`、`gen_ai.usage.output_tokens`）、`gen_ai.execute_tool`（`gen_ai.tool.name`、guard decision）。平行工具的 span 需正確掛在同一個 turn span 底下。
- Metrics：`equitylens.agent.loop.turns`（histogram）、`equitylens.agent.tool.calls`（counter，tag tool/status）、`equitylens.agent.guard.denials`（counter，tag reason）、`equitylens.agent.lease.lost`（counter）、`equitylens.agent.node.interrupted`（counter）。

### 9.4 前端（可選，最低優先）

若時間允許：在現有 agent run 詳情頁（Prestige 設計系統）新增「LLM Turns」分頁，列出每輪 thoughtSummary、工具呼叫（含被拒絕者）、預算消耗。沿用既有元件與 `src/assets/prestige-design.css`；不要新增 ECharts 註冊點。

---

## 10. Phase 5：Eval 與 A/B

### 10.1 Harness 擴充（`evals/`）

- `client.py`：`_investigate()` 保留完整 `detail.json()`；`build_request` 支援 `--engine`（寫入 request body `engine`）。
- `schema.py`：`RunOutput` 新增精簡 `trajectory`：`workflow_type`、`node_types`、`planner_modes`、`plan_rejected_count`、`llm_turns`、`tool_calls_by_name`、`guard_denials`、`web_calls`、`stop_reason`、`node_durations_ms`、`interrupted_nodes`。**不要**把整份 Blackboard 存進 `responses.jsonl`。
- `scorers.py`（確定性）：
  - `web_policy_violation`：`sourcePolicy == LocalOnly` 卻有 Web 檢索節點或 `search_web` 成功呼叫。
  - `planner_fallback`（v1）：`PlannerProposed` 事件 payload `mode == "DeterministicFallback"`（值見 `PlannerInvocation.cs`）。
  - `budget_exhausted`（v4）：`stop_reason == "BudgetExhausted"`。
  - 彙總：`planner_fallback_rate`、`web_policy_violation_count`、平均 `llm_turns`、平均 `tool_calls`、`guard_denial_rate`。
- `cli.py run`：新增 `--engine {DynamicDag,AgentLoop}`；`run_meta.json` 記錄 engine。
- `report.py`：報告新增「Trajectory」區塊。
- `cli.py rescore` 必須能從既有 `responses.jsonl` 重算新指標（舊檔沒有 trajectory → 指標為 null，不可報錯）。
- 測試（`evals/tests/`，全部離線）：以造好的 API JSON fixture 測試 trajectory 解析、`web_policy_violation`、`planner_fallback`、舊格式相容。

### 10.2 A/B 執行（需要本機 API 與 token；無法執行時記錄為阻塞並略過）

```bash
python -m equitylens_eval run --cases cases/research_v1.jsonl --mode investigation --engine DynamicDag --label v1-dag
python -m equitylens_eval run --cases cases/research_v1.jsonl --mode investigation --engine AgentLoop --label v4-loop
python -m equitylens_eval compare runs/v1-dag runs/v4-loop
```

結果與報告存到 `docs/eval-verification/<日期>-agent-loop-ab/`（依 `docs/eval-verification` 既有格式；**不要** commit `exports/`）。

### 10.3 決策門檻（寫進報告結論）

v4 取代 v1 作為預設的條件（全部成立）：
- `pass_rate` ≥ v1，且 0 題由 PASS 退步為 FAIL
- `hallucination_rate` 不高於 v1；`web_policy_violation_count == 0`
- p95 延遲 ≤ v1 × 1.3；平均成本 ≤ v1 × 1.5

未達標：維持 `DynamicDag` 為預設，報告列出差距與下一步。

---

## 11. 故障與安全情境總表（測試對照）

| 情境 | 預期 | 測試位置 |
|---|---|---|
| Worker 執行中當機（節點 `Running`） | 新 worker 接手；可恢復節點從 checkpoint 續跑 | §5.3、§6.8 |
| 重新認領時租約仍有效 | 訊息不 ack、不遺失 | §5.1 |
| 長節點超過 5 分鐘 | heartbeat 續約；不被雙重執行 | §5.2 |
| 舊 worker 失去租約後嘗試寫入 | fenced，寫入失敗 | §5.2 |
| LLM 回傳壞 JSON | 回饋錯誤；連續 2 次 → ProtocolFailure 優雅結束 | §6.8 |
| LLM 要求不可見工具 / 違反政策 | Guard 拒絕並記錄 | §6.8 |
| LLM 傳入原始數字給計算工具 | Guard 拒絕 | §6.8 |
| 預算耗盡 | 停止並強制起草一次 | §6.8 |
| 引用懸空 / 政策違反的答案 | OutputGate 阻擋 → Revise → 仍失敗則拒答 | §7.5 |
| 副作用工具 | 必經審批；參數變更即失效；只執行一次 | §8.2 |
| 兩人同時審批 | 只有一個成功 | §8.2 |
| 其他使用者讀取 run | 拒絕 | §7.5 |

---

## 12. 實作順序與優先序（夜間執行用）

| 優先 | 工作 | 對應驗收 | 必要性 | 預估 |
|---|---|---|---|---|
| 1 | Phase 1：ack 修正、租約 heartbeat/fencing、中斷節點接手 | A1–A3 | **MUST** | 1.5–2h |
| 2 | Phase 2：工具/Guard/Budget/Ledger/Loop/Checkpoint + LLM turn 紀錄 | A5–A8、A11 | **MUST** | 3h |
| 3 | Phase 3：`ResearchAgent` workflow、OutputGate、品質路由、引擎切換 | A4、A9、A10 | **MUST** | 2h |
| 4 | Phase 5.1：harness trajectory 與 `--engine`（離線部分） | A12 | **SHOULD** | 1h |
| 5 | Phase 4 A：工具審批 + 測試工具 | A13 | **SHOULD** | 1.5h |
| 6 | §9.3 OTel span/metrics | A14 | **SHOULD** | 0.5h |
| 7 | Phase 4 C：結果審閱節點 | A15 | COULD | 1h |
| 8 | Phase 5.2：實際 A/B（需 API） | A16 | COULD | 視環境 |
| 9 | 簡化 DAG：把 OutputGate + Critic 四節點合併為單一 `QualityGate` 節點（內部仍重用既有 Critic 邏輯），`ResearchAgent` 縮為約 6 個節點；既有 Critic 相關測試必須維持通過 | — | COULD | 1.5h |
| 10 | Phase 4 B、§9.4 前端 | — | COULD | — |

**每一列完成就 commit + push。** 任何時間點中止，分支上都應該是可建置、測試全綠的狀態。

### 12.1 整體完成定義（Definition of Done）

- [ ] 任務簡報驗收清單中所有 MUST 項目（A1–A11）皆有測試證明，A17 成立
- [ ] `dotnet test` 測試總數 ≥ 開工前數量（記錄於 progress 檔）
- [ ] migration 名稱有意義
- [ ] `evals` 的 `pytest` 全綠
- [ ] `AGENTS.md`、本文件（若有偏差，於 §15 記錄）、progress 檔已更新
- [ ] 所有 commit 已 push 到 `feat/research-agent-runtime`

---

## 13. 三工作流重構現況

本文件前段保留 v4 初期計畫的歷史背景。現行實作與驗收以 [收斂規格](remove-dynamic-dag.md)、[補充規格](remove-dynamic-dag-addendum.md) 及 [進度表](remove-dynamic-dag-progress.md) 為準。

| 項目 | 初始基準 | 收斂後 |
|---|---|---|
| 工作流 | 9 | ResearchAgent、PortfolioDiagnosis、CriticReview，共 3 |
| catalog node type | 54 | 13 |
| Services/Agents 行數 | 11,674 | 7,550（R10 提交點，後續以進度表終驗為準） |
| 編排 | 可動態擴增圖 | 固定 DAG，研究／投組共用節點內迴圈 |

研究包含品質檢查及最多一次修訂；投組包含六個任務型工具與固定人工審批；Critic 僅評分，不改原答案。品質階段及工具結果先保存，恢復重用已完成結果。圖拓撲、條件邊、租約 fencing、狀態機與 outbox 保留。

方法設定由角色內 load_skill 載入，保存版本與 hash；不再有 capability 或 supporting skill 清單。統一路由只決定角色和參數，明確入口與既有追問免分類，所有路徑仍檢查權限和能力邊界。

舊圖唯讀；續跑操作回 409，worker 取得有效租約後安全終止。新 investigation 僅接受 AgentLoop；舊回答仍可離線 rescore。本次只交付重構分支，未合併 main 或部署。後續是否增加並行工具、其他研究方法或大型任務分解，須另案驗收。

## 14. 風險與取捨

| 風險 | 緩解 |
|---|---|
| 單一 Agent context 膨脹 → 成本、延遲上升 | Ledger 摘要化、歷史精簡、token 預算 |
| LLM 路線不固定 → eval 變異增加 | trajectory 指標；每題可跑多次（harness 後續擴充）；Revise 只一次 |
| 灰箱內每步 Blackboard snapshot 消失 | `AgentLlmTurn` + `AgentToolCall` + checkpoint 取代 |
| 兩套引擎並存期間維護成本 | 以 A/B 決策門檻限定並存期間；新功能只加在 v4 |
| JSON 行動協定比原生 function calling 脆弱 | 解析失敗回饋 + 連續失敗上限 + 協定測試；未來可在 `IChatCompletionService` 加原生 tools 支援 |
| 數字可追溯檢查誤判（單位換算） | 先列 Warning，交給 Critic；參考 eval `normalize.py` 的換算規則；KI 記錄 |

### 與 v1 的取捨摘要

| | v1 Dynamic DAG | v4 Agent Loop |
|---|---|---|
| LLM 決策粒度 | 選 node type 長圖 | 選工具、參數、輪數、起草時機 |
| 安全來源 | plan validator | 工具可見性 + Guard + 預算 + OutputGate + 審批 |
| 恢復粒度 | 節點之間（中途當機 → run 失敗，Phase 1 修正後可接手重跑） | 迴圈每一輪 |
| 單 run 內平行 | 無 | 同輪工具呼叫平行 |
| HITL | 節點層級 | 節點 + 工具呼叫層級（參數綁定、冪等） |
| 稽核紀錄 | nodes / events / toolCalls / snapshots | 上述 + LLM turns + guard decisions + checkpoints |

---

## 15. 設計偏差紀錄（實作者填寫）

> 實作時若與本文件不同，在此記錄：章節、原設計、實際做法、理由。

| 章節 | 原設計 | 實際做法 | 理由 |
|---|---|---|---|
| §5.2 | EnsureOwned + 節點續約 | DbContext 非同步 SaveChanges 統一交易 fencing；executor 全期間以獨立 scope 續約 | 涵蓋 handler 中途 SaveChanges 與 planner；避免檢查和寫入之間的競態及共用 DbContext |
| §6 | 每輪 checkpoint 與最後三筆快照 | 每 node / attempt 最新 checkpoint，turn/tool 保留歷史 | 簡化恢復唯一鍵，手動 Retry 不重用舊輪次 |
| §6.6 | compute_growth | 暫不開放計算工具 | 目前 evidence 為文件片段，無可信結構化 metric 輸入 |
| §7.2 | claims[].numbers；多數失配才 blocking | 直接解析答案與同句引用，支援仟元；貨幣數值失配一筆即 blocking | 不依賴自報 claims；實測發現其他正確數字會稀釋嚴重單位錯誤 |
| §8.2 | 擴充單一 /approval API | 復用既有 approvalId 專屬 approve/reject API；每輪最多一個副作用審批 | 使用既有 request-id 與並發控制；避免多筆 Pending unique key 衝突 |
| §6 prompt | 可選 prompt registry | 固定 template ID/version/hash | 先交付可重現版本 |
| §9.3 | loop turns histogram | 新決策 counter，另有延遲 histogram | 區分決策數與時間分布；DB 為稽核真值 |
| §5.3 | MaxRetryCount 限制接手 | 至少一次接手，之後受 MaxRetryCount + 1 上限 | 預設 retry=0 的既有節點也必須符合 A2 的一次故障恢復 |

### 驗收證據索引（2026-11-03 更新）

前文是開工時的設計與驗收要求，未勾選的原始 checklist 不代表目前全未完成。以下對應實作證據與限制；不是新的測試執行或新的 A/B 成績。完整執行紀錄見 [progress](research-agent-runtime-progress.md)。

| 項目 | 可查證證據 | 範圍與限制 |
|---|---|---|
| A1 | [AgentRunWorkerTests](../../tests/EquityLens.Api.Tests/Services/Agents/AgentRunWorkerTests.cs)：`ProcessNextAsync_AcknowledgesOnlyDurableOutcomes` | 租約被占用／執行中失去租約不 ack |
| A2 | [AgentExecutorBoundaryTests](../../tests/EquityLens.Api.Tests/Services/Agents/AgentExecutorBoundaryTests.cs)：`ExpiredLease_RecoversInterruptedNode`、`RepeatedInterruption_StopsAtBudget` | 中斷恢復有上限；非冪等節點不盲目重跑 |
| A3 | [AgentLeasePostgresTests](../../tests/EquityLens.Api.Tests/Integration/AgentLeasePostgresTests.cs) | 真實 PostgreSQL 的拒寫、過期續約、獨立 heartbeat、並行 claim 與長執行驗證 |
| A4、A9、A10 | [ResearchAgentWorkflowTests](../../tests/EquityLens.Api.Tests/Services/Agents/ResearchAgentWorkflowTests.cs)：`FixedWorkflow_AcceptsOrRevisesOnce_WithoutCallingPlanner`、`OutputGate_BlocksInvalidEvidence` | 固定圖不呼叫動態 planner；修訂再驗收；正式預設已經使用者授權切成 AgentLoop，覆蓋 A4 原先預設 v1 的要求 |
| A5–A8 | [ResearchAgentLoopTests](../../tests/EquityLens.Api.Tests/Services/Agents/ResearchAgentLoopTests.cs) 與 [ResearchAgentLoop](../../src/EquityLens.Api/Services/Agents/Loop/ResearchAgentLoop.cs) | 平行工具、來源／參數 guard、已提交結果重用、協定與預算上限有測試。07:11 在 `test/research-loop-no-progress` 補上兩個 NoProgress 專項案例：空結果停止後恢復不再呼叫；重複證據最多最後一次起草。該檔 20 項通過，尚未合併 main |
| A11 | 同上 `TwoTurns_RecordDecisionsEvidenceAndToolResults`；[正式 UI 驗收](../eval-verification/2026-11-03-ui-smoke/README.md) | turn 與工具可查詢；UI 成本僅涵蓋已記錄迴圈，不是完整供應商帳單 |
| A12 | [test_trajectory.py](../../evals/tests/test_trajectory.py)、[test_pipeline.py](../../evals/tests/test_pipeline.py)；[題目範圍核對](../eval-verification/2026-11-02-agent-loop-ab/scope-audit-20261103.json) | 舊輸出無 trajectory 時表示未知，不冒充零違規；原始題庫計分八題 |
| A13 | [ToolApprovalTests](../../tests/EquityLens.Api.Tests/Services/Agents/ToolApprovalTests.cs)、[ToolApprovalPostgresTests](../../tests/EquityLens.Api.Tests/Integration/ToolApprovalPostgresTests.cs) | 測試工具驗證參數綁定、拒絕、恢復冪等；正式 Research Loop 不註冊副作用工具 |
| A14 | `Telemetry_ParentsParallelToolsToTurn_AndUsesBoundedMetricTags`（ResearchAgentLoopTests） | 驗證 span 親子關係及有限 metric tags；不是正式 dashboard 告警驗收 |
| A15 | 尚未實作研究結果依風險自動觸發人工審閱 | 工具執行審批與投組報告發布批准不等於此項 |
| A16 | [A/B 報告](../eval-verification/2026-11-02-agent-loop-ab/README.md) | 兩輪 v1 6/8、v4 7/8；同一開發集，未取得 holdout 泛化證據 |
| A17 | [部署後遠端 checks](../eval-verification/2026-11-03-portfolio-scenarios/release-checks.json)、progress 各版本測試紀錄 | 本地完整後端 977（含隔離 PostgreSQL）、前端 124；CI success 不自動代表 PostgreSQL 測試無 skip。migration 無 pending 的既有紀錄不冒稱每次文件 commit 都重跑 |

另需保留的界限：A7 恢復會先處理 pending turn 未提交部分，再進入下一輪，不能直接跳過整輪。已呼叫但未落盤的唯讀工具可能重呼叫；A13 的外部效果去重依賴下游冪等，不是任意 API 的 exactly-once。A9 的相容性指既有輸出契約；harness 已另行擴充 engine 與 trajectory，並非所有程式完全未變。

---

## 附錄 A：面試說明重點（30 秒版）

- **問題**：原本的 agent 是「LLM 規劃 DAG、程式驗證後長出節點」，安全但沉重，LLM 實際空間小，且有租約/中斷恢復的可靠性缺口。
- **決策**：改為「固定粗粒度 workflow + 自主 tool-using agent + 確定性安全邊界」。控制結果與邊界，不控制路線。
- **關鍵設計**：工具可見性執行 source policy；數字不經 LLM 搬運；每輪 durable checkpoint；租約 heartbeat + fencing；副作用工具審批綁定參數 hash 與冪等鍵；OutputGate 驗收引用與數字。
- **證據**：故障注入測試（中斷、租約遺失、重複恢復）、eval harness A/B（pass rate、幻覺率、政策違規、成本、延遲），以門檻決定是否切換預設引擎。
