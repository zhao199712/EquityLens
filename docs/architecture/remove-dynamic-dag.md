# 移除動態 DAG（LLM 規劃圖）— 任務規格

> 前置文件：[`research-agent-runtime.md`](research-agent-runtime.md)（v4 Agent Runtime 設計）、[`research-agent-runtime-progress.md`](research-agent-runtime-progress.md)（已完成 A1–A14）。
> 本文件只寫**目標、決策、邊界、驗收清單**。怎麼做由實作者讀完程式碼後決定；偏離本文件時在 §7 記錄理由。
> 進度記錄在 [`remove-dynamic-dag-progress.md`](remove-dynamic-dag-progress.md)。

---

## 1. 目標

移除「LLM／規則動態規劃並長出 DAG 節點」的整套機制，系統只保留**一種**編排模式：

> **固定、粗粒度的 DAG（生命週期）＋ 節點內受控的 Agent 迴圈 ＋ 確定性安全邊界。**

保留並沿用：durable runtime（狀態機、租約／heartbeat／fencing、中斷接手、outbox、Redis 佇列）、條件邊、節點與工具層級的人工審批、事件／`AgentToolCall`／`AgentLlmTurn`、OTel、eval harness。

## 2. 已定案的決策（不要更改）

| # | 對象 | 決策 |
|---|---|---|
| D1 | **研究問答**（`ResearchInvestigation` v1） | 移除。`POST /api/research/investigations` 一律建立 `ResearchAgent`（v4）。`engine` 欄位：省略或 `AgentLoop` → v4；`DynamicDag` → `400`，訊息說明已停用。 |
| D2 | **使用者回饋修訂**（`FeedbackRevision`） | 併入 `ResearchAgent`：使用者回饋與原答案作為**修訂模式迴圈的輸入**，重用同一套迴圈、OutputGate、Critic。`POST /api/agent-runs/{id}/feedback` 的 API 契約與前端行為維持不變。 |
| D3 | **投組診斷**（`PortfolioDiagnosis`） | 改成 **Agent 迴圈**：風險數學（`PortfolioRiskMathCapabilities` 的 allowlist 函式）包成 `Compute` 工具，由 LLM 依品質缺口決定要補算哪些分析。**數值只能由工具從 Blackboard / DB 讀取**，LLM 只能給有 schema 限制的設定參數。既有確定性步驟（載入脈絡、重用已持久化 Risk Run、歸因、品質評估）可保留為固定節點或包成工具，由實作者決定。**always-on 人工審批關卡與核准／拒絕分支維持不變。** |
| D4 | **研究品質審查**（`ResearchQualityReview`） | 改為固定 DAG：載入既有研究 run → Critic → 品質路由 → 條件式修訂（重用 `ResearchAgent` 修訂模式迴圈）→ OutputGate → 定稿。`POST /api/agent-runs/research-quality-review` 的 API 契約與前端行為維持不變。 |
| D5 | **舊資料**（已存在 DB 的上述四種舊 workflow run） | **保留唯讀**：列表、詳情、時間軸、節點、事件、工具呼叫照常可讀。對舊 run 的 retry／繼續執行回傳明確錯誤（建議 `409`，錯誤碼 `legacy_workflow_retired`，訊息提示用新流程重跑）。佇列中若撿到舊 workflow 的未完成 run：標記 `Failed` 並寫明確錯誤碼，**不可**當機或無限重試。不做資料遷移。 |
| D6 | 不受影響 | `CriticReview`、`DraftRevision`、`EvidenceRemediation`、`EvidenceReanalysis`（固定流程）照常運作；其節點 handler 若仍被任何流程使用就保留。 |

## 3. 要移除的機制（驗收以「沒有任何參照」為準）

- `LlmAgentWorkflowPlanner`、`DeterministicDynamicWorkflowPlanner`、`IAgentWorkflowPlanner`（動態規劃用途）、`PlannerInvocation`
- `DynamicPlanValidator`、`GraphMaterializer`、`DynamicPlanProposal` 等提案型別
- `AgentDynamicPlanCoordinator` 與 executor 中呼叫它的路徑；`DynamicPlanningTriggers`
- `orchestrationMode = "DynamicStateful"` 及執行期修改 `WorkflowDefinitionJson` 的邏輯
- `NodeCapabilityRegistry` / `INodeCapabilityRegistry`；`WorkflowSkillCatalog` 只保留**新流程實際用到**的部分（例如某個 skill 的 system prompt 若被迴圈使用），否則一併移除
- `AgentLoopController` 及其 loop policy（停止條件改由各 Agent 迴圈的預算／停止原因負責）
- 只被上述機制使用的節點 handler、常數、Blackboard key、prompt 範本、測試
- Admin 介面中依賴 skill／capability 的 API 與前端頁面：移除，或改成列出**工具註冊表**（工具名稱、風險等級、可見條件）。前端不能留下呼叫已刪除 API 的頁面。

> 注意：**拓撲排序（`AgentWorkflowPlanner` 的排序用途）、`AgentRunGraphValidator`、條件邊屬於固定 DAG 執行，必須保留。** 只刪「動態長圖」，不刪「執行圖」。

## 4. 邊界（不可違反）

1. 不修改、不 commit `src/EquityLens.Api/appsettings.json`；不新增會產生真實外部副作用的工具。
2. 不跳過、停用或為了變綠而刪除仍有意義的測試；被刪除機制專屬的測試可以刪，但**對應的行為若仍存在，要有新測試覆蓋**。
3. 狀態變更只能透過 `AgentStateMachine`；新 node type 同時註冊 DI 與 `AgentWorkflowCatalog`。
4. Source policy 由結構保證（工具不可見＋呼叫時拒絕）；數字不經 LLM 搬運。
5. 本地文件搜尋沿用 `search_document_chunks(...)`。
6. `GET` 類 API 對舊 run 的回應格式不得破壞（前端舊 run 詳情頁要能打開）。
7. 不 merge 到 `main`、不部署。週四有面試，正式站與 `main` 維持現狀。
8. 不在 commit、程式碼、文件中寫入模型識別名稱。

## 5. 驗收清單（每項都要有自動化測試或可重現指令證明）

| # | 驗收條件 | 優先 |
|---|---|---|
| R1 | `grep -rnE "DynamicStateful\|LlmAgentWorkflowPlanner\|DeterministicDynamicWorkflowPlanner\|DynamicPlanValidator\|GraphMaterializer\|AgentDynamicPlanCoordinator\|PlannerInvocation\|NodeCapabilityRegistry\|AgentLoopController" src tests` 只剩 `Migrations/` 或明確標示為 legacy 唯讀的對應（並在 §7 說明） | MUST |
| R2 | 建立研究 investigation 一律走 `ResearchAgent`；`engine: "DynamicDag"` 回 400 | MUST |
| R3 | 回饋修訂（D2）：送出 `NeedsCorrection` 回饋 → 子 run 使用 Agent 迴圈修訂，回饋內容出現在迴圈輸入，輸出經 OutputGate 與 Critic；API 回應形狀不變 | MUST |
| R4 | 研究品質審查（D4）：Accept 路徑跳過修訂；Revise 路徑執行一次修訂並寫回 `revisedAnswer`；API 回應形狀不變 | MUST |
| R5 | 投組診斷（D3）：LLM 透過 Compute 工具選擇補算項目；工具拒絕 LLM 直接提供的數值資料；重用既有 Risk Run 的行為保留；品質缺口驅動補算有上限（預算／輪數）並有停止原因 | MUST |
| R6 | 投組診斷人工審批：核准 → 定稿；拒絕（需附理由）→ run 失敗並帶理由；審批期間 worker 不被佔用（沿用既有 durable suspension） | MUST |
| R7 | 投組診斷的每輪 LLM 決策、工具呼叫（含被拒絕者）、checkpoint 與研究迴圈同樣可查詢、可中斷續跑；**重用**既有迴圈持久化／guard／checkpoint 基礎設施，不另寫一套 | MUST |
| R8 | 舊 run（D5）：詳情 API 可讀；retry 回 `409 legacy_workflow_retired`；佇列撿到舊 workflow 未完成 run 時標記 `Failed` 並帶錯誤碼，不拋未處理例外 | MUST |
| R9 | 使用者隔離：其他使用者無法讀取或操作新流程的 run | MUST |
| R10 | Admin：不再有 skill／capability 相關 API；若提供工具註冊表檢視，前端頁面可正常載入；前端 `npm run build` 與 `npm test` 通過 | SHOULD |
| R11 | Eval harness：`--engine` 預設與文件更新為只有 `AgentLoop`；舊 `responses.jsonl` 仍可 rescore；`pytest` 全綠 | SHOULD |
| R12 | 文件：`AGENTS.md` 的 V3 Agent Runtime／Research Agent Loop 章節、`research-agent-runtime.md` §13 Roadmap 更新為新現況；寫一段「移除前後」對照（刪除行數、節點數、編排模式） | SHOULD |
| R13 | 若環境可用：以 `evals/cases/research_v1.jsonl`（計分 8 題）跑 `--mode investigation`，結果存 `docs/eval-verification/<日期>-remove-dynamic-dag/`，與最近一次 AgentLoop 結果比較，確認沒有退步 | COULD |
| R14 | 每次 commit：`dotnet build`、`dotnet test`（含 PostgreSQL 整合測試，方式見 progress 檔）、`has-pending-model-changes`、`evals` 的 `pytest` 全部通過 | **每次 commit** |

## 6. 執行方式

- 分支：從 `ccr-1010e030-k3jmgi` 切出 `refactor/remove-dynamic-dag`，所有 commit push 到此分支。
- 建議順序：R8（舊資料防護先做，之後刪程式才不會讓舊 run 當機）→ R2 → R4 → R3 → R5–R7 → R1（最後清理）→ R10–R13。
- 每完成一項：寫測試 → 跑 R14 → 更新 progress 檔 → commit → push。任何時間點分支都要可建置、測試全綠。
- 刪除前先用 `grep` 確認沒有其他使用者；刪除後在 progress 檔記錄刪了哪些檔案與行數。
- 卡住時記錄在 progress 檔「阻塞」區，跳到下一個不依賴它的項目；不要停下來等人。
- 結束前填寫 progress 檔的「早上給 Reviewer 的摘要」。

## 7. 設計偏差紀錄（實作者填寫）

| 章節 | 原規格 | 實際做法 | 理由 |
|---|---|---|---|
| | | | |
