# 收斂為 3 個工作流 — 實作進度

> 對應規格：[`remove-dynamic-dag.md`](remove-dynamic-dag.md) 的 R1–R27，以及 [`remove-dynamic-dag-addendum.md`](remove-dynamic-dag-addendum.md) 的 R28–R36。主規格決策與邊界仍有效；角色／方法與路由分層依補充 D9、D10、D14 細化。

## 開工基準

| 項目 | 值 |
|---|---|
| 開工 commit | `8903e48` |
| 開工時 `dotnet test` 通過數 / 總數 | 最近基準 979/979；R1 後實跑 986/986，0 skipped |
| 開工時 `evals` `pytest` 通過數 | 75 |
| 開工時前端 `npm test` 通過數 | 124 |
| 可建立的工作流數 | 9 |
| catalog node type 數 | 54（新增情境節點已納入） |
| `Services/Agents` 行數 | 11,674 |
| 分支 | `refactor/three-workflows` |

## 驗收項目狀態

狀態：⬜ 未開始 ・ 🟨 進行中 ・ ✅ 完成 ・ ⛔ 阻塞 ・ ⏭ 略過 ・ 🔁 無法重現 ・ ❌ 驗收未通過

### Part A：收斂架構

| # | 驗收條件（摘要） | 優先 | 狀態 | 證明 | Commit | 備註 |
|---|---|---|---|---|---|---|
| R1 | 舊資料唯讀、retry 409、佇列安全失敗 | MUST | ✅ | 版本／定義及持久化節點、舊圖唯讀／409／安全終止／隔離 | 0e307d4、d47b0f6 | R10 已完成舊版本／節點形狀退役總驗收 |
| R2 | 研究一律 ResearchAgent；DynamicDag → 400 | MUST | ✅ | 預設 AgentLoop；DynamicDag 建立前拒絕 | e96d17a | controller 明確回 engine_retired；初始資料不再借用舊 provider |
| R3 | qualityGate 合併 | MUST | ✅ | 接受／修訂一次／拒答／Critic stage 恢復 | a5302d2 | 新版本研究／Critic 舊圖由 R1 判定唯讀 |
| R4 | 回饋修訂併入 ResearchAgent | MUST | ✅ | 子 run／requestId 去重／回饋種子 | 8bbc2e0 | 研究詳情改選 ResearchAgent 接收回饋 |
| R5 | 投組 Agent 迴圈與工具 | MUST | ✅ | 六工具執行／注入防護／持倉不變 | 7ea3e2c | 補充規格到達後，先驗收 R31、R30 再接續 |
| R6 | 投組人工審批不變 | MUST | ✅ | 等待釋放租約、無 wake、核准／拒絕與去重 | 7ea3e2c、98dd7e5 | |
| R7 | 迴圈基礎設施共用、可續跑、可查詢 | MUST | ✅ | 共用 loop；決策與結果先落盤；中斷重用 | 624d07d、7ea3e2c | R5–R7 已提交並驗收 |
| R8 | 刪除 4 個多餘工作流與前端呼叫 | MUST | ✅ | 建立 endpoint 白名單與舊結果唯讀 | 04629e1、d47b0f6 | 建立入口與 DI 移除；相依型別 R10 一起清理 |
| R9 | Ask Agent 路由到 3 個工作流 | MUST | ✅ | 單次 Router、明確入口及追問零分類 | 05d0363 | |
| R10 | 移除型別零參照 | MUST | ✅ | 只剩 migrations 與兩處 legacy 名稱對應 | d47b0f6 | |
| R11 | 3 個工作流、node type ≤ 15 | MUST | ✅ | FixedArchitectureTests：3 workflows／13 catalog／13 handler DI | d47b0f6 | |
| R12 | 使用者隔離 | MUST | ✅ | 研究、投組、列表、conversation 去重與 approval 所有權 | 05d0363、d47b0f6、d4f0a26 | |
| R13 | Admin 介面 | SHOULD | ✅ | 移除 registry API／頁面／路由；Admin 只管理固定圖 | d47b0f6 | |
| R14 | Eval harness | SHOULD | ✅ | CLI AgentLoop only、舊 responses 可 rescore | e2f3959 | |
| R15 | 文件與前後對照 | SHOULD | ✅ | AGENTS／架構 §13／進度實測對照 | e2f3959、終驗提交 | |

### Part B：實測問題

| # | 問題 | 優先 | 狀態 | 重現測試 | 修正測試 | Commit | 備註 |
|---|---|---|---|---|---|---|---|
| R16 | B1 OutputGate 誤判年份／代號 | MUST | ✅ | OutputGate_ExcludesContextNumbersButKeepsFinancialClaims：3 個正案例先失敗 | 5 個正反案例、拒答事件與 artifact、UI 新舊圖及原因顯示 | 64613f2 | 不忽略代號同值的財務金額 |
| R17 | B3 情境偷換＋澄清 | MUST | ✅ | 轉入既有持股原本 scenario_invalid | 守恆／其他持股／錯誤目標／替換拒絕／回覆取保存值 | 84cd17e | 自然語言路由澄清於 R34 再驗收 |
| R18 | B2 優化建議 | MUST | ✅ | 無情境的優化建議原本仍 passed | 提交 Guard、足夠／不足資料 gate、四指標與差異取同快照 | 98dd7e5 | |
| R19 | B4 路由重試＋log | MUST | ✅ | 見下方本項修正前失敗紀錄 | 完整 R27 與永久回歸測試 | 05d0363 | |
| R20 | B8 拖累正負號 | SHOULD | ✅ | 見下方本項修正前失敗紀錄 | 完整 R27 與永久回歸測試 | 0923b51 | |
| R21 | B6 VaR 樣本不足 | SHOULD | ✅ | 見下方本項修正前失敗紀錄 | 完整 R27 與永久回歸測試 | b346fdf | |
| R22 | B5 數字口徑 | SHOULD | ✅ | 見下方本項修正前失敗紀錄 | 完整 R27 與永久回歸測試 | d0dd186 | |
| R23 | B7 產業分類 | SHOULD | ✅ | 見下方本項修正前失敗紀錄 | 完整 R27 與永久回歸測試 | b6efae2 | |
| R24 | B9、B10 前端 | COULD | ✅ | 見下方本項修正前失敗紀錄 | 完整 R27 與永久回歸測試 | d4f0a26 | |
| R25 | B11 log 等級 | COULD | ✅ | 見下方本項修正前失敗紀錄 | 完整 R27 與永久回歸測試 | a6cdc44 | |

### 驗證

| # | 驗收條件 | 優先 | 狀態 | 證明 | Commit | 備註 |
|---|---|---|---|---|---|---|
| R26 | 8 題 eval 不退步 | SHOULD | ✅ | 31a6a2b 整套 7/8，等於舊基準；0 parse_error | 本次終驗提交 | docs/eval-verification/2026-11-03-three-workflows-final-eval/ |
| R27 | 全部驗證全綠 | 每次 commit | ✅ | 每項完整 R27；最後紀錄見下方 | 各項提交 | 每項重新執行 |

### 補充規格：角色、方法、來源與路由

| # | 驗收條件（摘要） | 優先 | 狀態 | 證明 | Commit | 備註 |
|---|---|---|---|---|---|---|
| R28 | 共用基礎＋角色＋本角色 skill 目錄＋已載入全文；唯讀 load_skill、至少 3 個 skill、載入上限與可追溯紀錄 | MUST | ✅ | 角色隔離、上限、hash、prompt 全文、拒絕越權 | d8301ea、d47b0f6 | 與 R9 一併做；保留法說會原 prompt，刪 Supporting skill；不能擴大權限；記錄 id／版本／雜湊及 trajectory |
| R29 | Router 只選工作流與參數，不選 skill／capability | MUST | ✅ | Router schema 不含 skill／capability | 05d0363 | 與 R9 一併做；取代先前 Router 選 playbook 的方案 |
| R30 | 投組任務型工具 ≤ 8；37 個數學函式不可直接呼叫；重用情境計算 | MUST | ✅ | 6 任務工具；37 函式無工具可見性 | 1298ffa | R5–R7 完成前先驗收；load_skill 不計入 8 個上限 |
| R31 | 研究角色來源規則與可確定性檢查的 OutputGate | MUST | ✅ | 來源歸屬、風險頁及錯置 gate 回歸 | 624d07d | R16 後優先補做；直接／補充來源不可錯置，表格／目錄／封面不得充當風險或實質證據 |
| R32 | 台積電／鴻海風險、法說會新題庫與原始 PDF 核實紀錄；validate 通過 | SHOULD | ✅ | 2 題 PDF 原文＋渲染核實，validate 通過 | 649ac38 | R26 前；至少各 1 題，Garage 原始 PDF 核對文件、實體頁碼、摘錄前保持 verified: false；與原 8 題分開計分 |
| R33 | 新題庫 investigation、skill trajectory、拆解實驗 A（MaxTurns=1）與 C | COULD | ⛔ | 未付費實跑；A/C 狀態及預算限制已記錄 | 44e63d2 | R32 核實後、R26 前或一起做；沿用累計 US$10 預算，無法確認剩餘額度先記錄阻塞 |
| R34 | 入口／追問免 LLM 路由、程式訊號、權限與參數驗證、低信心及不支援情境澄清 | MUST | ✅ | 入口／追問／不支援情境／權限／低信心測試 | 05d0363 | 與 R9 一併做；每種情況均有測試，不猜投組或替換情境；混合問題單 run 不跨角色 |
| R35 | 合併對話與 Router 為一次 LLM 呼叫，輸出 D14 結構 | SHOULD | ✅ | 對話到 workflow 一次模型分類；固定入口零次 | 05d0363 | 與 R9 一併做；保護直接回覆、澄清、拒絕、重置、修訂上次 run；明確入口仍為零次路由呼叫 |
| R36 | 約 20–30 句 Router 題庫，程式評分與準確率／混淆矩陣 | SHOULD | ⛔ | 26 題＋程式 scorer／混淆矩陣工具；live 未執行 | 44e63d2 | R26 前或一起做；涵蓋研究、投組、混合、模糊、越權、無投組與超出情境能力；不用 LLM judge |

## 更新後執行順序

R1–R26、R28–R32、R34–R35 均完成。R26 修正後完整一輪 7/8，與舊基準相同；全部輪次保留，累計保守費用上界 US$0.783291，低於本次獨立 US$1。R33／R36 live 仍受原預算限制；題庫／離線工具已交付。

`R1 → R2 → R3 → R4 → R16 → R31 → R30 → R5–R7 → R17 → R18 → R6 再驗收 → R8 → R9＋R28＋R29＋R34＋R35 → R19 → R10 → R11 → R12 → R13–R15 → R20–R25 → R32 → R33（環境與預算允許）＋R36 → R26`

- 每項仍須完整 R27 後更新進度、commit、push；未完成不標為通過。R9 的路由重試實作與 R19 共用，R19 再以重現／回歸、遮罩 log 驗收。
- D14 第 0 層：研究頁、投組診斷入口、既有對話追問直接決定角色，不呼叫 LLM 路由。第 1 層提供有權限的投組／ticker 訊號；模糊情況才進第 2 層 LLM；所有路徑皆須第 3 層驗證。
- 新題庫只採目前可信且最新的台積電（2330）與鴻海（2317）財報；原始 PDF 在 Garage。不能以模型記憶或搜尋摘要代替 PDF 核實。未核實題目保持 `verified: false`，不併入原 8 題 pass_rate。
- 不重新啟用排程、不合併 main、不部署；既有未提交程式碼保留，主規格與進度表不從遠端覆蓋。

## 驗證紀錄

| 時間 | 完成項目 | `dotnet build` | `dotnet test`（通過/總數） | `has-pending-model-changes` | `pytest` | 前端 build/test |
|---|---|---|---|---|---|---|
| 2026-11-03 09:20 | R1 防護基礎 | PASS | 986/986，0 skipped | 無變更 | 75 passed | build PASS／124 passed |
| 2026-11-03 | 補充規格與排程同步（含未提交迴圈草稿的工作目錄） | PASS | 994/994，0 skipped；隔離 PostgreSQL | 無變更 | 75 passed | build PASS／126 passed |
| 2026-11-03 | 最後完整 R27（final-release） | PASS | 879/879，0 skipped；隔離 PostgreSQL | 無變更 | 83 passed | build PASS／127 passed |

## 刪除清單

| 檔案 | 行數 | 理由 |
|---|---|---|
| `src/EquityLens.Api/Services/Agents/AgentDynamicPlanCoordinator.cs` | 181 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/AgentLoopController.cs` | 256 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/ClaimSetValidation.cs` | 105 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/DraftRevisionAgent.cs` | 180 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/DraftRevisionNodeHandlers.cs` | 220 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/DynamicPlanValidation.cs` | 346 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/DynamicWorkflowPlanning.cs` | 492 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/EvidenceAssessor.cs` | 92 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/EvidenceReanalysisWorkflow.cs` | 234 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/EvidenceRemediationAgent.cs` | 188 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/EvidenceRemediationNodeHandlers.cs` | 430 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/EvidenceRemediationWorkflowDefinitionProvider.cs` | 51 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/EvidenceRetrievalPlanAgent.cs` | 73 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/FeedbackRevisionWorkflow.cs` | 154 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/PlannerInvocation.cs` | 90 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/PortfolioDiagnosisNarrativeAgent.cs` | 78 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/ResearchInvestigationWorkflow.cs` | 474 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Api/Services/Agents/SourcePolicyRules.cs` | 35 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Web/src/services/agentRegistryAdmin.ts` | 39 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Web/src/views/admin/AgentRegistryView.vue` | 217 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `src/EquityLens.Web/src/views/admin/__tests__/AgentRegistryView.test.ts` | 129 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/AgentRunExecutorLoopPlanningTests.cs` | 141 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/DynamicWorkflowPlanningTests.cs` | 476 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/EvidenceAssessorTests.cs` | 154 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/EvidenceReanalysisWorkflowTests.cs` | 111 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/EvidenceRemediationWorkflowTests.cs` | 445 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/EvidenceRetrievalPlanAgentTests.cs` | 47 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/LlmDraftRevisionAgentTests.cs` | 123 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/PlannerInvocationTests.cs` | 223 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/PortfolioDiagnosisLoopTests.cs` | 141 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/ResearchQualityReviewLoopPolicyTests.cs` | 115 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |
| `tests/EquityLens.Api.Tests/Services/Agents/SourcePolicyRulesTests.cs` | 76 | 已刪機制／入口及其專用測試；仍有效行為由固定圖、工具與相容性測試覆蓋 |

## 收斂前後對照（R15）

| 指標 | 收斂前 | 收斂後 |
|---|---|---|
| 可建立的工作流 | 9 | 3 |
| catalog node type | 54 | 13 |
| `Services/Agents` 行數 | 11,674 | 7,712（遞迴計算所有 .cs，含 Loop；減少 3,962，約 33.9%） |
| 編排模式 | Static／Stateful／DynamicStateful | 固定 DAG＋節點內 Agent loop |

## 阻塞

| 項目 | 原因 | 已嘗試 | 建議 |
|---|---|---|---|
| R33／R36 live | 歷史外部費用缺完整帳務與 embedding 覆蓋，剩餘 US$10 無法確認 | 檢查舊 progress、A/B manifest、provider usage 與本機 usage snapshots；初期明示未完整記錄 | 提供累計費用覆蓋後在隔離服務執行，不增加上限 |
| R33 實驗 C | 引用報告未含 C 操作定義 | 搜尋 docs 中拆解實驗／MaxTurns；只有補充條文引用 | 取得具體對照條件，不自行改來源安全規則 |
| Garage 原始檔 | 目前 bucket 為空，DB key 回 NoSuchKey | 本機原始 PDF 核實；台積電與 DB hash 相符 | 發布前恢復原始檔儲存及下載驗證，另案授權操作 |

## 給 Reviewer 的摘要

交付分支：`refactor/three-workflows`，基準 `8903e48`。所有實作與驗證提交推至同分支；未合併 main、未部署、未新增 migration、未改正式資料，也未恢復排程。`src/App.vue` 與 `appsettings.json` 相對基準零差異。

**已完成**：R1–R26、R28–R32、R34–R35。三工作流／13 種 node type／13 個正式 handler；共用 durable loop、六個投組任務工具、三個研究工具與共用 load_skill、三個方法設定檔。原 planner capability 與 Supporting skill registry 已移除。Services/Agents 由 11,674 減為 7,712 行（含 Loop 子目錄），約減少 33.9%。

**R26 結果**：`31a6a2b` 原 8 題整套 7/8，恢復舊基準，0 JSON 解析錯誤。除保證獲利題外，其餘七題均通過。保證獲利題有有效引用與否定保證報酬的回答，但仍被原禁詞 scorer 判失敗；不改規則、不宣稱 8/8。所有中間輪次保留，未拼接最佳答案；開發集重複使用，不能推論 holdout／統計穩定性。五輪 LLM 保守估價合計 US$0.283291，加 embedding 預留 US$0.50，上界 US$0.783291，未超過本次獨立 US$1。完整 R27 911/911 後端、pytest 83、前端 127、build／EF 全過。

**未完成**：R33 補充題／拆解實驗與 R36 真實 Router 準確率仍未執行；本次授權不涵蓋它們，歷史 US$10 累計仍不完整。Router 26 題與評分工具已交付，0/26 observed、accuracy:null。C 的操作定義也未在引用報告中找到。

Part B：B1 年份／代號誤擋與拒答呈現、B2 優化須有情境、B3 轉入持股不偷換、B4 Router 重試及遮罩 log、B5 口徑揭露及模擬數值不冒充歷史估計、B6 每期限有效視窗門檻、B7 產業大類與原分類保存、B8 負貢獻過濾及空清單、B9 問題摘要、B10 空投組不呼叫風險、B11 正式 SQL Warning 均有回歸。這些是隔離驗證結果，未聲稱正式站已更新。

相容性與限制：舊 ResearchAgent／CriticReview／投組及退役工作流依版本與節點形狀唯讀；GET 保留且新增摘要欄位，續跑回 409。新的歷史 VaR／ES 在樣本不足時為 null，外部消費端需支援缺值。產業正規化使新風險輸入 hash 改變，舊 Risk Run 可能不再可重用。風險偏好無資料欄位時回 unavailable，不虛構偏好。新情境追問缺參數會澄清。財報題庫仍是狹窄開發集，沒有全面能力結論。

原始 PDF：R32 以本機原檔逐頁核實；台積電 hash 與 DB 相符，鴻海 DB 無原 hash。Garage 目前 bucket 為空，發布前需要獨立恢復與下載核驗，這次沒有上傳或資料遷移。

建議 merge／部署順序（僅建議，本次未執行）：

1. Review 本分支及最終 live 逐題對照／Router not_run 報告，R26 已恢復 7/8；確認費用覆蓋後完成 R36；R33 依可選優先度及具體實驗條件執行。
2. 備份部署版本、DB、Redis 工作佇列與 outbox、Garage 物件及配置，記錄可回復映像。確認原始檔可下載。
3. 排程維護時間，停止舊 worker 及新的 run 建立，等待有效租約排空。清查未完成舊圖，保留唯讀並透過新版相容層安全終止；不要改寫定義，不讓新舊 worker 混跑。
4. 使用者核准後才合併 main 並部署同版 API／worker／前端；驗證三工作流、研究接受／修訂／拒答、追問、投組情境、審批暫停與核准／拒絕、跨使用者隔離、舊 run GET 與 409、工具／租約恢復、空投組、Production log 設定。
5. 若需回復，先停止新版 run 建立與 worker，保留事故後資料快照，再回復舊映像及一致的 DB／佇列／物件快照。新版圖不可交給舊 worker；沒有 schema migration 不代表可混跑兩版。回復後核對新增資料保留／重建方式，避免覆蓋未備份工作。


## 實作紀錄

- R1：版本相容性判定注入正式 DI；執行、retry、回饋、兩種審批入口有防護。HTTP exception filter 將退役錯誤轉為 409。GET 保持既有 fallback，不要求歷史節點仍存在 catalog。
- worker 先取得租約及 fencing，再終止退役圖；WaitingForFeedback／WaitingForApproval 的舊圖同樣可安全終止，不執行 handler、不寫新的 wake。
- 按使用者補充，D3 已加入投組任務型工具上限 8 個、首批 6 個；底層數學函式不對模型公開。
- 無正式環境／DB 變更，無模型呼叫。R1 完整驗證記錄在本機 `/tmp/r1-*.log`；不把本機 log 納入 Git。

### R2 研究入口收斂

- 新建研究固定使用 ResearchAgent；明確 DynamicDag 在 controller 回 400、service 在建立資料前拒絕。忽略舊預設引擎設定，保留其餘 loop 預算設定。
- ResearchAgent 自行建立初始 Blackboard 與 run；retry 初始資料同樣使用新 provider。既有舊圖 provider 待 R10 移除，不再由研究建立入口呼叫。
- 修正前選擇引擎測試確認兩項失敗；修改後完整 R27：後端 build PASS、986/986（0 skipped，隔離 PostgreSQL）、EF 無模型變更、pytest 75、前端 build PASS／124 tests。新增 API 費用 0。

### R3 品質節點合併

- ResearchAgent 版本 2：7 個固定節點，接受時跳過兩個修訂節點；CriticReview 版本 2：3 節點。共用 QualityGate，評分模式不改來源研究答案。
- 合併階段保留各自 input／output，以 node key＋execution attempt 存進 Blackboard。每階段與結果同次 fenced save；已提交評論在恢復時不重呼叫。只有 finalizer 發布 run.OutputJson，避免中間報告冒充最終結果。
- 原證據輸入／輸出測試改查階段紀錄；保留圖驗證、缺 handler、缺持久化 node、狀態機、失敗 retry、引用及 Critic 行為覆蓋。新增粗粒度圖契約與恢復驗證。
- 完整 R27：build PASS、987/987（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 75、前端 build PASS／124 tests。無外部 API 費用。
- 當時補充方案為 4 個 Lead skill 改 playbook、Router 選工作流＋playbook＋參數；此路由方案已由後續補充 D9／D10／D14 取代，按下方更新紀錄執行。

### R4 回饋修訂併入研究

- 回饋修訂現在建立 ResearchAgent 子 run，固定 reviseAgent → reviseQualityGate → finalizeResearch，不重新走意圖辨識、不啟動動態規劃。
- 只複製父 run 的必要研究資料與證據；不繼承品質階段 checkpoint。原答案與使用者回饋出現在模型輸入且明示不是證據，仍須引用工具取得的來源。建立時保存 revisionSeed，retry 保留相同回饋與證據。
- 原 requestId 去重、父子研究／Agent 關聯及回應形狀保留；研究詳情頁定位新工作流供回饋使用。
- 兩項新增／調整測試先確認失敗再修正。完整 R27：build PASS、988/988（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 75、前端 build PASS／124 tests。外部 API 費用 0。

### R16／B1 OutputGate 誤判

- 重現：有同句引用的 EPS 答案，包含年份、日期／股票代號、題目已提供的金額時，3 個正案例修正前全被擋下；2 個錯誤財務金額案例仍被正確攔截。
- 加入語境數字排除，保留金額單位、比率算式與引用檢查；不把股票代號 2330 的排除規則套到 2330 元。
- 最終拒答或系統風險說明保存 quality_gate_rejected、被擋數字、ResearchRun.ErrorMessage 與 QualityGateRejected 事件；前端明確區分執行完成與答案未通過。品質階段支援新粗粒度圖與歷史圖。
- 完整 R27：build PASS、993/993（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 75、前端 build PASS／126 tests。無模型呼叫，費用 0。

### 2026-11-03 補充規格同步

- 從 `origin/ccr-1010e030-k3jmgi`（`7397f81`）只取 `docs/architecture/remove-dynamic-dag-addendum.md`，未合併分支、未覆蓋主規格與本進度表。新增 R28–R36 並按補充文件建議插入執行順序。
- D9／D10／D14 將先前 playbook 路由方案細化為：Router 只決定固定角色與參數；Agent 在迴圈內自行透過 `load_skill` 載入本角色方法。工具／來源政策／審批邊界不能被 skill 放寬。
- 原主規格中的 playbook 字樣此次不改寫；實作以新補充的角色／方法分層為準，並保留原決策與安全邊界。這是使用者更新規格，並非擅自偏離規格。
- 本次提交僅含補充規格與進度表，迴圈草稿保留未提交。完整 R27 在目前工作目錄通過（含草稿新增 1 個測試），不代表 R5／R7／R30 已完成驗收；無外部 API 呼叫。補充檔 blob 與遠端一致，主規格相對 HEAD 無差異。

### R31 來源角色規則

- 研究 prompt 補回 Primary／Supporting 來源歸屬、風險題與文件頁面規則；證據視窗實際提供 DocumentType／SourceRole。
- 確定性 gate 檢查明確年報冒稱法說會、目錄／封面起始標記、風險題引用沒有風險內容的財報表格；不以寬泛關鍵字替代 Critic 的語意充分性判斷。
- 同次提交 R7 的共用迴圈核心抽離與結構化工具結果恢復基礎；投組工具／圖／審批尚未註冊，R7 仍進行中。
- 完整 R27：build PASS、1001/1001（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 75、前端 build PASS／126 tests。無外部 API 呼叫。

### R30 投組工具邊界

- 六個任務型唯讀工具，分析只能選 Core／TailRisk／RiskContribution；從授權 DB 或持久化 Blackboard 取得數值，不接受模型提供價格、報酬、持倉或計算結果。
- 情境工具重用既有 PortfolioScenarioCalculator 與快照擷取，不建立另一套數學。測試核對保存結果 hash／指標、跨使用者拒絕及沒有修改來源 Blackboard。
- 所有 37 個底層函式均無工具可見性，逐一驗證 Guard 拒絕。尚未註冊為投組 workflow，接續 R5–R7。
- 完整 R27：build PASS、1009/1009（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 75、前端 build PASS／126 tests。無模型呼叫。

### R5–R7 固定投組與共用迴圈

- PortfolioDiagnosis 升版為 4，固定六個持久化節點（單一路徑執行五個）；工具選擇在 PortfolioAgentLoop 內完成，不再靠動態圖補分析。
- 研究／投組使用同一 AgentLoopEngine，保留決策先落盤、工具結果先落盤、checkpoint、工具審批、預算／停止原因與 Guard 拒絕紀錄。投組結構化事實與 Blackboard patch 亦可從已保存結果恢復；使用不同角色 prompt／工具集。
- PortfolioQualityGate 檢查 P 編號引用及數字，輸出數字直接讀保存工具資料；不通過的模型文字不發布，明示 Limited，保留已計算資料供審閱。後續 R18 加入優化情境必要條件。
- WaitForHumanApproval 固定節點透過既有 approvals API 提交，WorkflowGate 的核准／拒絕都恢復執行，再走對應條件分支；拒絕理由保留、run Failed。測試涵蓋等待時沒有租約／wake、requestId 去重、他人不可審批、拒絕必填及兩分支。
- 六個工具均有執行測試：快照／Risk Run 重用／數學／歸因／情境／未保存風險偏好的明確 unavailable；持倉與交易不變。現有資料模型沒有使用者風險偏好欄位，因此不虛構偏好。
- 移除 DynamicWorkflowPlanningTests 中 3 個投組動態規劃專屬測試（70 行）；由固定圖、工具選擇、審批與恢復測試接替仍存在的行為。舊情境 handler 測試暫以獨立 fixture 保留，R10 移除 handler 時再移至工具層。
- 完整 R27：build PASS、1014/1014（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 75、前端 build PASS／126 tests。沒有外部 API 呼叫。

### R17／B3 轉入既有持股

- 重現測試先證明指定轉入台積電被舊 cash-only 計算器拒絕。新增可選 transferToTicker；holding 僅把減碼所得轉入另一檔既有持股，cash 舊契約不變。
- 保留其他持股股數及原現金；不接受同一標的、缺目標、未知標的、借款或增加減碼來源權重。分數權重的十進位精度以微小誤差界檢查淨值守恆，現金比例取原快照。
- 工具 Guard 比對使用者已確認情境，不接受模型將 holding 偷換為 cash 或更改來源權重；情境報告的轉入對象／權重直接由持久化結果生成，不發布模型另述的配置。路由自然語言與澄清在 R34 全面驗收。
- 完整 R27：build PASS、1020/1020（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 75、前端 build PASS／126 tests。外部 API 費用 0。

### R18／B2 優化必須有情境證據

- 重現：只建議降低集中度、未模擬任何情境，舊 gate 仍 Passed。現在優化意圖或明確情境請求必須有 Calculated 比較，否則輸出 Limited 與未能計算改善幅度的原因。
- 已有足夠快照時，提前 submit_answer 會被 Guard 拒絕並回饋 simulate_weight_scenario 要求；已落盤決策保留，接續下一輪。預算耗盡不把未比較的建議視為通過。
- 回覆直接呈現同一快照原組合／調整後的年化波動、VaR、ES、最大回撤與差異，並標出樣本數、信心水準與 horizon；資料不足不生成改善幅度。
- R6 再驗收：核准／拒絕／等待釋放租約／無 wake／跨使用者及去重測試再次通過。完整 R27：build PASS、1023/1023（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 75、前端 build PASS／126 tests。無外部 API 呼叫。

### R8 建立入口與前端收斂

- 移除四種舊流程的 HTTP POST、service 方法、provider DI 與前端建立按鈕／API；Critic 保留既有入口，歷史詳情繼續唯讀。
- 移除只驗證已刪除建立入口／專屬工作流的測試與 rate-limit 清單項，加入建立 endpoint 白名單、評論仍可讀且無舊建立操作的測試；研究修訂／隔離／retry 由固定流程既有測試保護。
- 暫留內部舊 provider／handler 型別，因仍與待刪動態 coordinator 共用；按順序在 R10 一併解開相依並總驗收，R8 不提早標完成。
- 完整 R27：build PASS、996/996（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 75、前端 build PASS／125 tests。無付費呼叫；未部署。

### R28 方法工具與 prompt 分層

- 三個部署 skill 設定檔：法說會要點、市場事件、投組風險優化；通用研究方法留在研究角色 prompt。法說會內容與原字串雜湊一致，有固定 hash 回歸驗證。
- 共用基礎＋固定角色＋本角色目錄摘要＋已載入全文。新增 skill 檔不用修改 Router 或程式；load_skill 走原工具紀錄／checkpoint 路徑，保存 id、版本、內容 hash。方法內容不進證據 Facts，不能當作引用來源。
- 同批多個載入也預先保留上限；跨角色與超限拒絕可查。既有來源政策仍限制工具，skill 最多縮小工具集合，不能放寬權限。預設每 run 最多 3 個，可由 Options 調整。
- eval trajectory 新增 loaded_skills（id／版本／hash，不把方法全文列成證據）；舊結果仍可解析。Supporting skill 舊 catalog 等待 R9 解開 Router 相依、R10 刪除後才標 R28 全數完成。
- 完整 R27：build PASS、998/998（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 76、前端 build PASS／125 tests。無外部 API 呼叫。

### R9／R29／R34／R35／R19 角色路由與對話整併

- 單一 Router 同時處理直接回覆、澄清、拒絕與建立工作流；D14 JSON 不接受 skill／capability 欄位。對話收到已驗證決策後直接建立，實測一次模型呼叫，沒有第二次分類。
- 研究／投組明確入口直接建立；既有 run 追問沿用角色及投組期間，重置／修訂由對話入口處理。公司候選由資料庫比對；原始問題中的年份／季度不交給模型改寫。
- 所有建立路徑仍檢查所有權、信心、標的與情境能力；多投組未指名即使模型猜中其中一個仍列選項澄清。外人投組拒絕並記錄，不把轉入既有持股偷換為留現金。
- B4 重現：從本項前的 HEAD 擷取原 Router 於獨立測試型別執行，MalformedOutput_ShouldRetryOnce 預期 2 次、實際 1 次（失敗）；新路由兩次失敗回澄清，warning 僅保留錯誤分類、固定欄位位置、白名單鍵與 JSON 型別，未知鍵遮罩，不記錄原文。暫時重現型別未納入產品；回歸測試保留。
- 對話 requestId 查詢先驗證 session 所有權，避免去重路徑洩漏他人訊息。測試涵蓋同一 requestId 跨使用者、單次分類、追問／重置零分類、未授權投組、未知股票、低信心與不支援情境。
- 完整 R27：build PASS、1014/1014（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 76、前端 build PASS／125 tests。無外部 API 呼叫。新追問若有尚未綁定的轉入情境會澄清，不猜目標權重。

### R10／R11／R12／R13 舊機制清除與總驗收

- 刪除動態 planner、validator、materializer、coordinator、舊迴圈 controller、capability 與 supporting skill catalog；executor 僅執行建立時保存的固定圖，不再擴增或改寫定義。保留拓撲排序、依賴檢查、條件邊與所有租約／fencing／outbox 機制。
- 正式 catalog 恰好三工作流、13 個 node type；DI 也恰好 13 種 handler。品質內部階段改為普通檢查元件；投組歸因／情境快照改為工具使用的唯讀計算元件。37 個風險函式保留獨立參數驗證與計算測試，移除節點能力描述。
- 舊型別名稱只剩 API 的 LegacyWorkflowReadModel.cs 與前端 services/legacyWorkflows.ts 唯讀對應（及 migrations）；R10 搜尋已核對。原 GET 的 nodes/events/toolCalls/definition 不需舊 catalog；三種現行流程的舊版本與舊節點形狀均會退役，持久化 node type 也納入判定。
- Admin registry API／路由／頁面移除，workflow 設定只呈現固定圖。歷史 run 的篩選與結果呈現留在集中相容層，沒有恢復建立入口。
- 刪除測試限於舊圖擴增、舊 planner 提案、已刪 provider／agent 與 registry 專用契約。仍有效的證據檢查、回饋修訂、投組數字、來源政策、風險數學、情境所有權／現金／幣別、審批、恢復與隔離改由固定圖及工具測試覆蓋。rerank fallback 的證據和診斷保存移至 search_local 結果；共用工具持久化保留該診斷。
- 完整 R27：build PASS、864/864（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 76、前端 build PASS／121 tests。R1／R8／R28 隨舊註冊與 Supporting skill 清除完成總驗收。沒有外部 API 呼叫、migration、部署或正式資料改動。

### R14／R15 Eval 與現況文件

- Live CLI 僅接受 AgentLoop，client 預設明確指定並覆蓋題庫舊 engine；非法引擎在 HTTP 前拒絕。舊 responses 的離線 rescore 與未知 trajectory 語意保留。
- 更新 AGENTS runtime／workflow 清單、架構文件 §13 與 eval README；根 README 未加入設計決策。收斂數字採實際 catalog／handler 與程式行數。
- 完整 R27：build PASS、864/864（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 78、前端 build PASS／121 tests。無外部 API 呼叫。

### R20／B8 拖累的正負號與空清單

- 新 gate 已在重構中排除正數／零貢獻，新增混合與全正案例證明只列負貢獻。UI 重現測試先失敗：完整歸因但沒有負貢獻仍顯示資料不足。
- 報告新增可選 attributionCoverage；完整資料顯示「本期沒有負貢獻持股」，未完整或舊報告明示涵蓋不足，不把沒有負值當成缺資料。
- 完整 R27：build PASS、866/866（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 78、前端 build PASS／122 tests。無外部 API 呼叫。

### R21／B6 各期限歷史樣本品質

- 重現：120 筆價格產生 119 筆日報酬，30 日只有 90 個滾動視窗，舊 API 仍回傳歷史 VaR −0.0235357。先以 Assert.Null 重現失敗。
- 個股與兩種投組計算路徑均揭露每期限有效視窗數／門檻／狀態；少於既有政策 100 筆時歷史 VaR、ES 回 null。模擬結果仍是每日資料校準的獨立方法，UI 明示不可替代歷史樣本品質。未強迫各期限 VaR 單調。
- UI 不把 null 當成 0%，原首頁小圖排除不可用的數字。報告快照保留每期限品質欄位。
- 完整 R27：build PASS、869/869（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 78、前端 build PASS／122 tests。初輪前端 nullable 編譯失敗已修復並重跑全部。無外部 API 呼叫。

### R22／B5 計算口徑與錯誤映射

- 重現測試證明 Risk Run adapter 會將模擬分位數塞入歷史 VaR／ES；移除此替代，來源沒有歷史估計時保持缺值。
- 同步 Risk API 新增 measurementBasis，Agent 每項數字保存 provenance，Risk Run 讀取回傳原請求 inputSnapshot；舊結果未記錄的權重／現金／樣本口徑一律未知，不補造。前端共用元件顯示期間、權重、現金、方法、樣本、信心與期限。
- 口徑差異詳見 portfolio-risk-measurement-basis.md。情境比較沿用同快照同方法的原組合／調整組合，四指標的差異不與其他頁面混算。
- 完整 R27：build PASS、870/870（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 78、前端 build PASS／124 tests。無外部 API 呼叫。

### R23／B7 產業彙總層級

- 重現：相同集中度用途的 ResolveIndustry 原樣回傳「電子工業」與「電子零組件業」，等值測試失敗。SecurityService 保存不同外部來源的 Industry／Sector；原本讀取只做 fallback，沒有層級正規化。
- 新共用 broad-industry-v1 將已知電子細類彙總為電子工業、已知金融別名彙總為金融保險業；未知原值不猜測。風險、歸因、回測輸入均使用，原分類另存 RawIndustry，不修改 Security 原始資料。
- 壓力測試仍依原始細分類決定半導體／金融衝擊，僅彙總分類改為一致。風險輸入 hash 會因新的分類快照改變；舊快照不改寫，必要時重新計算以取得新口徑。
- 原快照排序測試改為同時核對正規化類別與原始分類，仍驗證來源變動不改既存快照。完整 R27：build PASS、876/876（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 78、前端 build PASS／124 tests。無外部 API 呼叫。

### R24／B9、B10 列表摘要與空投組

- 兩個 UI 重現測試先失敗：列表沒有問題／標的，空 Risk Run 頁面仍呼叫計算。新增可選 questionSummary／ticker／portfolioName；列表支援顯示與搜尋，後端只在投組與 run 所有人相同時讀取名稱，摘要截斷 160 字，舊損壞 JSON 可讀。
- 投組詳情與 Risk Run 頁面先確認持倉，空投組不呼叫風險／治理計算，顯示尚無持倉。保留現金流估值用途。未修改 App.vue 導覽。
- 完整 R27：build PASS、877/877（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 78、前端 build PASS／127 tests。無外部 API 呼叫。

### R25／B11 正式 SQL log 設定

- 重現：正式設定沒有 Logging 分類，測試先失敗。僅在 appsettings.Production.json 設定 EF Database.Command／Infrastructure 為 Warning，保留其他設定。
- ASPNETCORE_ENVIRONMENT=Production 時由標準設定載入；本次只提交，不部署、不改 appsettings.json。
- 完整 R27：build PASS、878/878（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 78、前端 build PASS／127 tests。無外部 API 呼叫。

### R32 風險與法說會題庫

- 新增獨立 2 題：台積電匯率風險（財報實體第64頁）、鴻海核心獲利規模化（2026-05-14 法說會實體第12頁）。封面、全文摘錄及圖表渲染核對後才設 verified:true；validate：2 cases、0 problems、0 unverified。
- Garage 目前 bucket 為空、DB object key 回 NoSuchKey；改讀既有本機原始 PDF。台積電與 DB SHA256 完全一致；鴻海 DB 未存 hash，保留核對原檔 hash 與限制。詳見 docs/eval-verification/2026-11-03-risk-conference-cases/；PDF 不提交，也未上傳正式 bucket。
- 此項只驗收 gold，不宣稱新系統已答對。原八題及 excluded_reason 未動。完整 R27：build PASS、878/878（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 78、前端 build PASS／127 tests。無付費 API 呼叫。

### R33／R36 離線交付與付費阻塞

- Router 新題庫 26 題；獨立程式評分 action／workflow／參數，輸出兩種混淆矩陣及逐題結果；不使用 LLM judge。五個測試驗證缺測、錯參數、混淆矩陣、重複／未知 ID 與情境目的地。
- 因預算覆蓋不完整，真實分類未執行：0/26 observed、accuracy:null，不以 fixture 回覆冒充模型實測。新題 investigation、A／C 拆解亦未執行；狀態及恢復步驟已寫在報告。
- 完整 R27：build PASS、878/878（隔離 PostgreSQL，0 skipped）、EF 無變更、pytest 83、前端 build PASS／127 tests。無付費呼叫。本項只交付離線部分，R33／R36 不標全數完成。

### R26 舊基準重算與最終驗收

- 原題庫依 verified／excluded_reason 恰有 8 題；保存的 53b5db1 AgentLoop 回答用現行評分器離線重算仍為 7/8。逐題舊分數／新未測欄位、來源回答與題庫 hash 見 docs/eval-verification/2026-11-03-three-workflows/comparison.json。新 live 因預算阻塞，未聲稱不退步。
- 隔離資料 74 securities、241 documents、8,363 chunks／embeddings，與舊 manifest 筆數相同；尚未以此推定內容完全相同。原八題與補充兩題維持獨立。
- 終驗另補投組同批分析＋中斷恢復：獨立 MathResults 累積保留，已成功工具不再呼叫。廢棄名稱搜尋僅命中 migrations 與兩處集中 legacy 相容檔；App.vue／appsettings.json 相對基準零差異。

- 最後完整 R27（final-release）：後端 build PASS、879/879（隔離 PostgreSQL，0 skipped）、EF 無模型變更、eval pytest 83 passed、前端 build PASS／127 tests。git diff --check 通過。R26 live 仍阻塞，這些套件通過不替代模型 eval。

### 使用者追加原 8 題 live eval（2026-11-03）

- 使用者明確授權本次獨立 US$1；隔離 API 重啟至 404df16，原題庫 investigation／AgentLoop 一次，未改 scorer。
- 6/8（75%），基準 7/8（87.5%）。鴻海產品線新增失敗：研究／修訂合計 4 次 JsonObject 解析錯誤，最終資料不足；保證獲利題與舊基準同樣 citation_integrity=0。R26 不標完成。
- 原始回答、逐題結果、持久化軌跡、費用與比較保存在 docs/eval-verification/2026-11-03-three-workflows-live/。37 次 LLM 呼叫保守估價 US$0.052676，另留 embedding US$0.10；總上界 US$0.152676。未做額外重跑。
- 完整 R27（eval-live-final）通過：build、879/879 後端（0 skipped、隔離 PostgreSQL）、EF 無變更、pytest 83、前端 build／127 tests。測試結果不掩蓋 live eval 品質退步。
- 僅交付驗證產物與進度；未修改程式、合併 main、部署或修改正式 DB。

### R26 退步修正：動作協定與可恢復診斷

- 修正前新增 10 個重現案例全數失敗（`/tmp/protocol-repro-before.log`）：錯誤 answer／tool arguments／引用陣列形狀只得到泛用例外，JSON 語法錯誤可能回顯原始欄位名稱，兩角色缺少可解析的完整動作範例。
- 抽出共用 LoopProtocol，逐欄位驗證並回傳固定錯誤分類、欄位路徑、expected／actual 型別；parse_error 的 ActionJson 與事件僅保存診斷，不保存錯誤回覆原文。下一輪歷史包含可修正的欄位資訊，仍連續兩次失敗停止，不增加輪數或預算。
- prompt v5 加入完整合法 JSON 範例。已提交 v4 決策在恢復時保留原省略欄位的預設，不重問 LLM 或重做成功工具；新輸出使用嚴格格式。
- 檢查鴻海舊失敗 ledger：2025 產品別收入表在 E10、年報 112 頁，長 1,079 字元、相關性排序第 2。新回歸測試確認高排序表格在預算縮減證據視窗後仍包含完整內容與頁碼；未重現選取／截斷缺陷，因此沒有改檢索演算法或寫死題目／答案。
- 一般報酬承諾問句改以風險揭露為可回答範圍，提示先檢索並引用真實段落；沒有降低 gate、修改 scorer 或替安全回覆附上無關引用。
- 完整 R27（protocol-fix）：後端 build、891/891（隔離 PostgreSQL，0 skipped）、EF 無模型變更、pytest 83、前端 build／127 tests；targeted loop 40 tests。新增 12 個測試，包含錯誤後中斷恢復、舊決策恢復與證據視窗。
- R26 live 分數在重跑前仍維持 6/8 未通過；接著在同一獨立 US$1 上限的剩餘額度中重跑原 8 題，保存獨立報告。只改功能分支，不合併 main、不部署。

### R26 第二輪與股數列單位修正

- `9f60065` 原 8 題重跑為 6/8：鴻海恢復通過；EPS 因附加數字的單位追溯未通過；保證獲利題引用已通過，但否定句包含禁詞被既有 scorer 判失敗。完整中間結果保留於 docs/eval-verification/2026-11-03-three-workflows-protocol-fix/，未改題庫或規則。
- `unit-repro-before` 29 個數字案例中 3 個失敗：2 個明示仟／千股的欄名未換算，1 個把同欄裸數字誤用為金額。修正按原文位置限定股數單位範圍，不擴散到金額／EPS 列；增加同值不同列、不明金額單位仍拒絕等回歸。
- prompt v6 明示即使 Critic 接受，也要處理 outputGate 阻擋；缺乏單位來源時移除不必要的附加數字或補證據。風險回答直接陳述風險，不複述承諾獲利前提。
- 完整 R27（unit-context-release）：build、899/899（隔離 PostgreSQL，0 skipped）、EF 無模型變更、pytest 83、前端 build／127 tests。此階段新增 8 個數字回歸。
- 前兩輪 LLM 加各輪 embedding 預留的累計上界 US$0.315680；最後重跑仍受同一 US$1 額度限制。R26 在最後重跑前仍為未通過。

### R26 第三輪與可追溯衍生比例

- `54bdfd3` 原 8 題重跑仍為 6/8，EPS 恢復通過；鴻海答案數值與來源相符，但受分號引用邊界、收入表頭單位及衍生年增率辨識限制而拒答。保證獲利的否定句仍觸發舊禁詞規則。完整中間結果保存於 docs/eval-verification/2026-11-03-three-workflows-fixed/。
- 新增一般格式重現案例：收入（百萬元）、同句分號後引用、由舊值增／降至新值的年增率。以精確來源運算元重算衍生百分比；缺少來源、零分母、錯誤比例、跨句號／換行引用均保持拒絕。未修改 scorer 或產生人工引用。
- 完整 R27（derived-context-release）：build、908/908（隔離 PostgreSQL，0 skipped）、EF 無模型變更、pytest 83、前端 build／127 tests。新增 9 個數字案例。
- 前三輪保守累計上界 US$0.475104；下一輪 LLM admission cap US$0.30，另保留 embedding US$0.10，總上界不超過原獨立 US$1。R26 待此版本整套原 8 題驗證，不拼接不同輪次結果。

### R26 第四輪與 PDF 黏連文字單位辨識

- `3d98904` 本輪 6/8；鴻海剩餘阻擋源自數值後接「元件及其他」被解析成「元」，導致表頭百萬元不能換算。3 個一般化重現案例在修正前失敗，修正後通過；不把產品名、股數／股東／股份當作單位後綴。
- 離線重播第三、四輪鴻海 4 份實際初稿／修訂稿及原引文，全部 Blocking=false，VerifiedRatioExpression／VerifiedGrowthExpression；原 live 分數保持 6/8，不使用離線結果替換分數。見 docs/eval-verification/2026-11-03-three-workflows-recheck/quality-replay.json。
- 完整 R27（unit-word-release）：build、911/911（隔離 PostgreSQL，0 skipped）、EF 無模型變更、pytest 83、前端 build／127 tests。前四輪累計保守費用上界 US$0.629965；最後重跑上限 LLM US$0.25＋embedding 預留 US$0.10，累計不超過 US$1。

### R26 最終整套驗收

- `31a6a2b` investigation／AgentLoop 原 8 題 **7/8**，與 `53b5db1` 保存基準相同。鴻海與 EPS 皆通過，0 parse_error；保證獲利否定句仍命中既有禁詞，維持失敗。題庫、scorer、排除條件均未變。
- 原始回答／軌跡、逐題比較、全部中間輪次與累計用量見 docs/eval-verification/2026-11-03-three-workflows-final-eval/。本輪 34 次 LLM 呼叫，保守估價 US$0.053326；五輪含 embedding 預留累計上界 US$0.783291。原授權剩餘額度沒有用於其他 eval。
- 完整 R27（final-eval-acceptance）：build、911/911（隔離 PostgreSQL，0 skipped）、EF 無模型變更、pytest 83、前端 build／127 tests；相較初始增加 32 個回歸案例。
- R26 標為完成；其他未測項維持原狀。未合併 main、未部署、未更動正式 DB、未恢復排程。

## 瀏覽器驗收後修正（2026-11-03）

### 財報比較數字追溯

- 使用正式站測試帳號保存的台積電回答及引用建立離線 fixture，不重新呼叫付費 API。修正前 9 個新增案例中 6 個失敗：正確差額被拒，部分錯算卻未阻擋。
- 新增同單位相減驗證；百分比相減須以百分點表達。已驗證計算結果只可在相同引用範圍內重用，不接受缺來源、錯誤單位、錯誤結果或跨來源借用。
- 原始失敗回答現在通過數字 gate；這是離線回歸，並非重新執行線上 eval，不改原 7/8 計分。
- 完整 R27：後端 920/920、0 skipped；eval pytest 83、前端 127，雙端 build 與 EF pending-model 通過。日誌 `/tmp/browser-math-r27-*.log`。
- 正式服務未更動；本階段新增 API 費用 0。未刪除檔案。

### Agent Run 長報告可見性

- 修正前測試重現：沒有 IntersectionObserver 回呼時，整份報告落在 `.kimi-reveal:not(.visible)` 的透明祖先內（1 failed / 9 passed）。正式站已保存超過 5,000px 報告透明的 DOM／截圖證據。
- 詳情頁移除整頁 ScrollReveal，結果一載入即顯示；保留既有版面及審批操作，不依賴視窗比例或捲動恢復位置。
- 完整 R27：後端 920/920、0 skipped；pytest 83、前端 128，雙端 build／EF 通過。`/tmp/browser-ui-r27-*.log`。無檔案刪除、無付費呼叫、無部署。

### 風險快照與壓力測試

- 查閱正式站唯讀 log，確認壓力測試 `BuildWorstTwentyDayStress` 有 DivideByZeroException；測試帳號沒有成功保存的快照。不能僅憑該 log 斷言快照 POST 的唯一失敗原因，但原快照確實依賴同一壓力計算且重算另一套模型。
- 修正前重現 7/7 failed：零／負價格、事件區間外價格、缺資料時未回報具體元件。修正後非正價格回 `invalid_prices`，歷史事件缺期間內價格回 `insufficient_prices`，不刪除壞資料再假裝有效；舊完整報告 API 仍拒絕不完整資料並列出缺少元件。
- 新增指定計算的快照 API；前端傳目前顯示的 Run ID。從授權 DB 保存原始結果、輸入／期間、模型、InputHash、fallback 及品質評估，不再重算。失敗品質保留 Failed，未驗證保留 Unvalidated，明示此快照範圍。讀取舊快照格式不變，不需要 migration。
- 新增他人／其他投組／未完成／缺結果／錯誤 operation 拒絕、快照不可變及不呼叫計算的測試。UI 先重現未傳 Run ID，修後也驗證清單刷新失敗仍顯示已成功保存。
- 完整 R27：後端 930/930、0 skipped；pytest 83、前端 129，雙端 build／EF 通過。`/tmp/browser-risk-r27-*.log`。無檔案刪除、無付費呼叫、無部署。

### 投組股票名稱與本輪終驗

- 修正前情境名稱測試失敗：模型提供「國建 2327 轉入台達電」會原樣顯示。現在由授權持股快照的 SecurityName／Ticker、驗證後權重及轉入對象產生名稱；缺名稱只顯示代號，不猜名稱。工具快照同時回傳 DB 名稱；模型原始輸入仍留在工具稽核紀錄。
- 回歸驗證國巨／台積電名稱、轉入既有持股、資產守恆、其他持股不變；原有負貢獻篩選測試仍通過。新路由 objective 沿用問題文字，沒有重新加入模型改寫目標。
- 完整 R27：後端 **931/931、0 skipped**（含隔離 PostgreSQL 整合測試），eval pytest **83**，前端 **129**；雙端 build 與 EF pending-model 通過。最後日誌 `/tmp/browser-final-r27-*.log`。
- 真實瀏覽器以本分支前端＋5535 隔離 API 驗證。僅複製自己的測試投組與保存結果到隔離 DB；付費 provider 金鑰停用，沒有重新生成 AI 回答。長報告深層連結／中段捲動／重新整理皆無透明祖先；指定 Risk Run 快照建立成功，SQL 比對保存 risk JSON 與原 Run ResultJson 完全相同、模型相同、資料日 2026-11-02。
- 瀏覽器證據：`exports/browser-smoke/2026-11-03-fixes/long-report.png`、`risk-snapshot.png`（依規範不 commit）。長報告使用歷史輸出測可見性，所以舊名稱與舊拖累內容仍保留，不能當作新流程生成結果。隔離 DB 未複製正式回測評估，故畫面 Unvalidated；Failed 狀態保存另有後端回歸測試。

### 給 Reviewer 的摘要：本輪追加

- 完成四項修正：引用算式差額／百分點追溯、長報告可見性、指定 Run 不重算的風險快照及壓力價格防護、可信股票名稱。沒有重寫歷史 run，也沒有新增工作流／node／工具。
- 仍為 3 workflows、13 node types；本輪未刪除檔案，無 migration、無外部 API 費用。原 8 題線上 eval 仍是前次 7/8，本輪是保存回答回放與完整 harness 測試，不宣稱新的線上分數。
- 未部署、未 merge main、未恢復排程；正式站仍是舊版本。發布時先後端再前端，因新前端快照呼叫新 endpoint。沿用前文備份／停止舊 worker／舊圖退役／smoke／回復步驟；額外 smoke 檢查指定 Run 快照模型、日期、品質與原結果一致。回退後新快照仍可由原 GET JSON 顯示。
