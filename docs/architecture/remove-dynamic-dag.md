# 收斂為 3 個工作流（移除動態 DAG）— 任務規格

> 前置文件：[`research-agent-runtime.md`](research-agent-runtime.md)（v4 Agent Runtime 設計）、[`research-agent-runtime-progress.md`](research-agent-runtime-progress.md)（已完成 A1–A14）。
> 本文件只寫**目標、決策、邊界、驗收清單**。怎麼做由實作者讀完程式碼後決定；偏離本文件時在 §9 記錄理由。
> 進度記錄在 [`remove-dynamic-dag-progress.md`](remove-dynamic-dag-progress.md)。
> 預計執行時間：一整個工作天，**做到底**。依 §7 的順序，每完成一項就 commit + push。

---

## 1. 目標

1. 移除「動態規劃並長出 DAG 節點」的整套機制，系統只保留**一種**編排模式：
   > **固定、粗粒度的 DAG（生命週期）＋ 節點內受控的 Agent 迴圈 ＋ 確定性安全邊界。**
2. 可建立的工作流**收斂為 3 個**，node type 從目前 53 種收斂到**約 12 種**（上限 15）。
3. 修正正式站實測發現的問題（§6，Part B）。

保留並沿用：durable runtime（狀態機、租約／heartbeat／fencing、中斷接手、outbox、Redis 佇列）、拓撲排序與圖驗證、條件邊、節點與工具層級的人工審批、事件／`AgentToolCall`／`AgentLlmTurn`、OTel、eval harness。

## 2. 目標架構

| 工作流 | 用途 | 固定 DAG（參考形狀） |
|---|---|---|
| **ResearchAgent** | 研究問答；**使用者回饋修訂**也走這裡 | `validate → detectIntent → researchAgent 🟨 → qualityGate → [reviseAgent 🟨 → reviseQualityGate]? → finalize`；回饋修訂的 run 從 `reviseAgent` 開始，以回饋＋原答案為輸入 |
| **PortfolioDiagnosis** | 投組診斷＋人工審批 | `loadContext → portfolioAgent 🟨 → portfolioQualityGate → approve 🧑 → finalize / finalizeRejected` |
| **CriticReview** | 只評分不修改（Admin 用） | `loadResearchRun → qualityGate（評分模式）→ finalizeReport` |

🟨 = 節點內 Agent 迴圈，**共用同一套**迴圈基礎設施（持久化、Guard、預算、checkpoint、工具審批、LLM turn 紀錄）。

`qualityGate` = 現在的 `ResearchOutputGate` ＋ Critic 四節點（`BuildEvidencePacket`、`CheckEvidence`、`CritiqueAnswer`、`FinalizeCriticReport`）＋ `RouteResearchAgentQuality` 合併為**單一節點**；Critic 的邏輯（`ICriticReviewAgent` 等）保留，只是不再拆成多個節點。

目標 node type（參考，可依實作調整，上限 15）：`Validate`、`DetectIntent`、`ResearchAgentLoop`、`QualityGate`、`FinalizeResearch`、`LoadResearchRun`、`FinalizeCriticReport`、`PortfolioLoadContext`、`PortfolioAgentLoop`、`PortfolioQualityGate`、`WaitForHumanApproval`、`FinalizePortfolio`、`FinalizeRejectedPortfolio`。

> 功能沒有消失，而是從 node 變成**工具**：檢索、rerank、取頁面、重用 Risk Run、風險數學、歸因、情境模擬。每個工具都要有測試。

## 3. 已定案的決策（不要更改）

| # | 對象 | 決策 |
|---|---|---|
| D1 | `ResearchInvestigation`（v1） | 刪除。`POST /api/research/investigations` 一律建立 `ResearchAgent`。`engine`：省略或 `AgentLoop` → v4；`DynamicDag` → `400`，訊息說明已停用。 |
| D2 | `FeedbackRevision` | 併入 `ResearchAgent` 修訂模式。`POST /api/agent-runs/{id}/feedback` 的 API 契約與前端行為不變。 |
| D3 | `PortfolioDiagnosis` | 改成 Agent 迴圈。風險數學、歸因、風險設定檔、**權重情境模擬**、重用既有 Risk Run 都包成工具。**投組工具以任務為單位，上限 8 個；37 個底層數學函式不得直接暴露給 LLM**，只由工具內部確定性組合執行。首批使用 `get_portfolio_snapshot`、`get_risk_report`、`run_risk_analysis`、`get_performance_attribution`、`simulate_weight_scenario`、`get_risk_profile` 六個工具；分析種類用 enum 限制，人工審批仍是固定節點。**數值資料只能由工具從 Blackboard / DB 讀取**；LLM 只能給有 schema 限制的設定參數（例如目標權重、轉入對象、期間）。**always-on 人工審批與核准／拒絕分支不變。** |
| D4 | `ResearchQualityReview` | **刪除**（沒有任何 UI 呼叫；功能已被 ResearchAgent 涵蓋）。移除 endpoint 與前端未使用的 service 函式。 |
| D5 | `DraftRevision`、`EvidenceRemediation`、`EvidenceReanalysis` | **刪除**（功能已被 ResearchAgent 的迴圈與修訂模式涵蓋）。移除 endpoint、前端呼叫與按鈕（`AgentRunDetailView.vue`、`AgentRunAdminDetail.vue` 等）。只被它們使用的節點、Agent、prompt、Blackboard key、測試一併移除。 |
| D6 | `CriticReview` | 保留，改用 `qualityGate`（評分模式）。`POST /api/agent-runs/critic-review` 與 Admin 研究管理頁行為不變。 |
| D7 | **舊資料**（D1、D2、D4、D5 以及舊版 PortfolioDiagnosis 已存在 DB 的 run） | **保留唯讀**：列表、詳情、時間軸、節點、事件、工具呼叫照常可讀（前端舊 run 詳情頁要能打開）。retry／繼續執行回 `409`，錯誤碼 `legacy_workflow_retired`。佇列撿到舊 workflow 的未完成 run：標記 `Failed` 並帶錯誤碼，**不可**當機或無限重試。不做資料遷移。 |
| D8 | **Ask Agent（聊天路由）** | 必須繼續運作，路由目標改為上述 3 個工作流。`InvestmentResearchRouting` 可保留或改寫，但不得依賴已移除的 skill／capability 機制。聊天 Router 僅輸出 Research／Portfolio 工作流、4 種 playbook 之一與標的／投組／情境參數；CriticReview 保留 Admin 入口。解析或 schema 錯誤重試一次，再失敗回澄清並記錄遮罩 warning。 |

## 4. 要移除的機制（驗收以「沒有任何參照」為準）

- `LlmAgentWorkflowPlanner`、`DeterministicDynamicWorkflowPlanner`、`PlannerInvocation`、動態規劃用途的 `IAgentWorkflowPlanner`
- `DynamicPlanValidator`、`GraphMaterializer`、`DynamicPlanProposal` 等提案型別、`DynamicPlanningTriggers`
- `AgentDynamicPlanCoordinator` 與 executor 中呼叫它的路徑；`orchestrationMode = "DynamicStateful"`；執行期改寫 `WorkflowDefinitionJson` 的邏輯
- `NodeCapabilityRegistry` / `INodeCapabilityRegistry`；`WorkflowSkillCatalog` 移除；7 個 Supporting skill 刪除，4 個 Lead skill 改為 playbook（研究方法 prompt、工具範圍、預算、必要輸入），保留法說會專屬 prompt。playbook 不能放寬來源政策或全域預算
- `AgentLoopController` 及其 loop policy（停止條件改由各 Agent 迴圈的預算／停止原因負責）
- D1、D2、D4、D5 的工作流、provider、endpoint、只被它們使用的 handler／Agent／常數／Blackboard key／prompt／測試
- Admin 中依賴 skill／capability 的 API 與前端頁面：移除，或改為列出**工具註冊表**（名稱、風險等級、可見條件）

> **保留**：拓撲排序、`AgentRunGraphValidator`、條件邊——這些是固定 DAG 執行需要的。只刪「動態長圖」，不刪「執行圖」。

## 5. 邊界（不可違反）

1. 不修改、不 commit `src/EquityLens.Api/appsettings.json`；不新增會產生真實外部副作用的工具（下單、寄信、真正修改使用者投組）。情境模擬只計算，不改資料。
2. 不為了變綠而跳過或刪除仍有意義的測試；被刪除機制專屬的測試可刪，但**仍存在的行為要有新測試覆蓋**。
3. 狀態變更只能透過 `AgentStateMachine`；新 node type 同時註冊 DI 與 `AgentWorkflowCatalog`。
4. Source policy 由結構保證（工具不可見＋呼叫時拒絕）；數字不經 LLM 搬運。
5. 本地文件搜尋沿用 `search_document_chunks(...)`。
6. 對舊 run 的 `GET` API 回應格式不得破壞。
7. **不 merge 到 `main`、不部署。** 週四面試，正式站維持現狀；由使用者 review 後決定。
8. 前端遵守 `AGENTS.md`（Prestige 設計系統、ECharts 只在 `TechChart.vue` 註冊、不加 bare `page` class）。另一分支 `origin/fix/frontend-public-ux` 正在修前端導覽列，**避免修改 `src/App.vue` 的導覽列區塊**以免衝突。
9. 不在 commit、程式碼、文件中寫入模型識別名稱。

## 6. Part B：正式站實測發現的問題

> 來源：另一個 session 以測試帳號在正式站實測（2026-11-03）。每一項**先寫重現測試確認問題存在**，再修；若無法重現，在 progress 檔記錄並跳過。如果某項因重構而自然消失，寫測試證明即可。

| # | 問題 | 期望行為 |
|---|---|---|
| B1 | **OutputGate 誤判**：把年份（2025）、股票代號（2330）、免責聲明當成無法追溯的數字，有引用的好答案被換成「資料不足」，run 卻顯示 `Succeeded` | 年份、日期、代號、題目中已出現的數字、免責聲明不納入數字追溯；被換成拒答時，artifact 狀態與事件要清楚標示原因（例如 `quality_gate_rejected` 與被擋的數字清單），不可看起來像正常成功 |
| B2 | **「如何優化波動」沒有得到建議**：路由理解了意圖，但沒有產生任何權重情境，報告無法回答調整後波動能降到多少 | 投組 Agent 迴圈遇到優化類問題時，要主動用情境模擬工具提出至少一組權重調整並回報調整前後的波動、VaR、ES、最大回撤 |
| B3 | **情境被偷換**：「移到台積電」被拆成三個轉現金情境，回覆卻說會轉入台積電；工具只支援轉現金 | 情境工具支援「轉入指定持股」與「轉現金」；若請求超出工具能力，回**澄清問題**，不得用不同情境代替。聊天回覆內容必須與實際計算的情境一致（測試比對） |
| B4 | **路由偶發「暫時不可用」且無紀錄**：推測是 LLM 輸出 JSON 型別不符（`InvestmentResearchRouting.cs` 的 `workflow_routing_unavailable`） | 解析失敗時重試一次並回饋錯誤；仍失敗則回澄清問題。**記錄 warning log**（含失敗原因、截斷後的輸出，不含個資或金鑰） |
| B5 | **同一組合不同頁面數字不一致**：最大回撤四種、年化波動兩種 | 找出各自的計算口徑（期間、權重基準、是否含現金）。統一口徑，或在 UI 與 API 標示各數字的期間與方法 |
| B6 | **30 日 VaR 絕對值小於 7 日 VaR** | 檢查視窗與樣本數；樣本不足時不輸出或標示「樣本不足」，不可輸出不合理數值 |
| B7 | **產業分類不一致**：國巨、奇鋐為「電子工業」，川湖為「電子零組件業」 | 找出資料來源差異；產業集中度計算使用一致的分類層級（必要時建立對照表） |
| B8 | **「主要拖累」列出大漲股票**：只取貢獻最低三檔，沒看正負 | 「拖累」只列貢獻為負的持股；沒有負貢獻時明確說明 |
| B9 | Agent Runs 列表看不出每個 run 問了什麼 | 列表顯示問題摘要／標的（研究：ticker＋問題；投組：投組名稱） |
| B10 | 空投資組合仍呼叫風險 API，回 400 並印 console 錯誤 | 前端在沒有持股時不呼叫，顯示空狀態 |
| B11 | 正式環境把每條 EF SQL 記成 Info log | 正式設定中 `Microsoft.EntityFrameworkCore.Database.Command` 等類別調為 `Warning`；放在非 `appsettings.json` 的設定檔或環境設定，並在文件說明 |

## 7. 驗收清單與執行順序

### Part A：收斂架構

| # | 驗收條件 | 優先 |
|---|---|---|
| R1 | 舊資料防護（D7）：舊 run 可讀；retry 回 `409 legacy_workflow_retired`；佇列撿到舊 workflow 未完成 run 時標記 `Failed` 並帶錯誤碼。**最先做**，之後刪程式碼才不會讓舊 run 當機 | MUST |
| R2 | 研究一律 `ResearchAgent`；`engine: "DynamicDag"` 回 400（D1） | MUST |
| R3 | `qualityGate` 合併完成；ResearchAgent 與 CriticReview 都使用它；Accept 路徑跳過修訂、Revise 路徑修訂一次並再驗收 | MUST |
| R4 | 回饋修訂併入 ResearchAgent（D2）：回饋內容出現在迴圈輸入；API 形狀不變 | MUST |
| R5 | 投組 Agent 迴圈（D3）：LLM 透過工具選擇分析項目；工具拒絕 LLM 直接提供的數值資料；重用既有 Risk Run 的行為保留；有預算／輪數上限與停止原因 | MUST |
| R6 | 投組人工審批：核准 → 定稿；拒絕（需理由）→ run 依既有語意結束並帶理由；審批期間不佔 worker | MUST |
| R7 | 投組迴圈與研究迴圈**共用**同一套持久化／Guard／checkpoint／LLM turn 基礎設施；中斷可續跑；每輪決策與工具呼叫（含被拒絕者）可查詢 | MUST |
| R8 | 刪除 D4、D5 的工作流、endpoint、前端呼叫與按鈕；前端沒有呼叫已刪除 API 的程式碼 | MUST |
| R9 | Ask Agent 路由到 3 個工作流正常運作（D8） | MUST |
| R10 | `grep -rnE "DynamicStateful\|LlmAgentWorkflowPlanner\|DeterministicDynamicWorkflowPlanner\|DynamicPlanValidator\|GraphMaterializer\|AgentDynamicPlanCoordinator\|PlannerInvocation\|NodeCapabilityRegistry\|AgentLoopController\|ResearchQualityReview\|FeedbackRevision\|EvidenceRemediation\|EvidenceReanalysis\|DraftRevision" src tests` 只剩 `Migrations/` 與明確標示的 legacy 唯讀對應（§9 說明） | MUST |
| R11 | 可建立的工作流恰好 3 個（E2E 測試專用者除外）；catalog node type ≤ 15 | MUST |
| R12 | 使用者隔離：其他使用者無法讀取或操作新流程的 run | MUST |
| R13 | Admin：無 skill／capability API；若提供工具註冊表檢視，頁面可正常載入 | SHOULD |
| R14 | Eval harness：`--engine` 只剩 `AgentLoop`（或移除參數並沿用預設）；舊 `responses.jsonl` 仍可 rescore；`pytest` 全綠 | SHOULD |
| R15 | 文件：`AGENTS.md`（V3 Agent Runtime、Research Agent Loop、workflow 清單）、`research-agent-runtime.md` §13 更新為新現況；寫「收斂前後」對照（工作流數、node type 數、`Services/Agents` 行數、編排模式） | SHOULD |

### Part B：實測問題

| # | 對應 | 優先 |
|---|---|---|
| R16 | B1 OutputGate 誤判 | MUST |
| R17 | B3 情境偷換＋澄清 | MUST |
| R18 | B2 優化建議 | MUST |
| R19 | B4 路由重試＋warning log | MUST |
| R20 | B8 拖累正負號 | SHOULD |
| R21 | B6 VaR 樣本不足 | SHOULD |
| R22 | B5 數字口徑一致或標示 | SHOULD |
| R23 | B7 產業分類一致 | SHOULD |
| R24 | B9、B10 前端列表摘要與空狀態 | COULD |
| R25 | B11 正式 log 等級 | COULD |

### 驗證與收尾

| # | 驗收條件 | 優先 |
|---|---|---|
| R26 | 以 `evals/cases/research_v1.jsonl`（計分 8 題）跑 `--mode investigation`，結果存 `docs/eval-verification/<日期>-three-workflows/`，與最近一次 AgentLoop 結果比較，確認沒有退步 | SHOULD |
| R27 | 每次 commit：`dotnet build`、`dotnet test`（含 PostgreSQL 整合測試）、`has-pending-model-changes`、`evals` 的 `pytest`、前端 `npm run build` 與 `npm test` 全部通過 | **每次 commit** |

**執行順序**：R1 → R2 → R3 → R4 → R16 → R5–R7 → R17 → R18 → R6 → R8 → R9 → R19 → R10 → R11 → R12 → R13–R15 → R20–R25 → R26。
（B1 緊接在 `qualityGate` 合併之後修，B2、B3 在投組迴圈完成後修，因為它們會在重構中被改寫。）

## 8. 工作方式

- 分支：從 `ccr-1010e030-k3jmgi` 切出 `refactor/three-workflows`，所有 commit push 到此分支。
- 每完成一項：寫測試 → 跑 R27 → 更新 progress 檔 → commit → push。任何時間點分支都要可建置、測試全綠。
- 刪除前先用 `grep` 確認沒有其他使用者；刪除後在 progress 檔記錄刪了哪些檔案與行數。
- 需要 migration 時命名要有意義；不要修改既有 migration。
- 卡住時記錄在 progress 檔「阻塞」區，跳到下一個不依賴它的項目；不要停下來等人。
- 結束前填寫 progress 檔「給 Reviewer 的摘要」：完成／未完成、收斂前後數字、已知風險、正式站實測問題的處理結果、建議的 merge 與部署步驟。

## 9. 設計偏差紀錄（實作者填寫）

| 章節 | 原規格 | 實際做法 | 理由 |
|---|---|---|---|
| D7 | 明列刪除的工作流及舊投組唯讀 | 舊 ResearchAgent／CriticReview 圖也依版本及節點辨識為唯讀 | 合併 QualityGate 會刪除其舊節點；不能繼續執行不再受支援的圖。 |
| R1 分階段實作 | 先建立舊資料防護 | 防護先以目前註冊 provider 的版本／catalog 判定，替換 provider 版本或移除註冊時立即生效 | 避免中途提交讓仍在替換中的工作流全部失效；最終只註冊三個新版本。 |
