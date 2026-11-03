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

狀態：⬜ 未開始 ・ 🟨 進行中 ・ ✅ 完成 ・ ⛔ 阻塞 ・ ⏭ 略過 ・ 🔁 無法重現

### Part A：收斂架構

| # | 驗收條件（摘要） | 優先 | 狀態 | 證明 | Commit | 備註 |
|---|---|---|---|---|---|---|
| R1 | 舊資料唯讀、retry 409、佇列安全失敗 | MUST | ✅ | WorkflowExecutionCompatibilityTests：7 passed；版本／節點辨識、四種非終態、租約、讀取／retry 隔離 | 本項提交 | 防護完成；隨後續 provider 更新啟用各舊版退役，R10 再總驗收 |
| R2 | 研究一律 ResearchAgent；DynamicDag → 400 | MUST | ✅ | 預設／AgentLoop 建立及隔離、停用引擎不落盤測試 | 本項提交 | controller 明確回 engine_retired；初始資料不再借用舊 provider |
| R3 | qualityGate 合併 | MUST | ✅ | 7／3 節點契約、接受／修訂一次／拒答、已提交 Critic 恢復不重呼叫 | 本項提交 | 新版本研究／Critic 舊圖由 R1 判定唯讀 |
| R4 | 回饋修訂併入 ResearchAgent | MUST | ✅ | 回饋 API 去重／父子關聯、三節點修訂圖、prompt 內容、retry 還原種子 | 本項提交 | 研究詳情改選 ResearchAgent 接收回饋 |
| R5 | 投組 Agent 迴圈與工具 | MUST | ✅ | 六工具執行／Guard／所有權／持倉不變測試 | | 補充規格到達後，先驗收 R31、R30 再接續 |
| R6 | 投組人工審批不變 | MUST | ✅ | | | |
| R7 | 迴圈基礎設施共用、可續跑、可查詢 | MUST | ✅ | 共用核心、決策／結果落盤及結構化工具結果恢復測試 | | R5–R7 已提交並驗收 |
| R8 | 刪除 4 個多餘工作流與前端呼叫 | MUST | ✅ | endpoint 白名單、舊結果可讀且無建立按鈕、R27 | 本項提交 | 建立入口與 DI 移除；相依型別 R10 一起清理 |
| R9 | Ask Agent 路由到 3 個工作流 | MUST | ✅ | | | |
| R10 | 移除型別零參照 | MUST | ✅ | | | |
| R11 | 3 個工作流、node type ≤ 15 | MUST | ✅ | | | |
| R12 | 使用者隔離 | MUST | ✅ | | | |
| R13 | Admin 介面 | SHOULD | ✅ | | | |
| R14 | Eval harness | SHOULD | ✅ | | | |
| R15 | 文件與前後對照 | SHOULD | ✅ | | | |

### Part B：實測問題

| # | 問題 | 優先 | 狀態 | 重現測試 | 修正測試 | Commit | 備註 |
|---|---|---|---|---|---|---|---|
| R16 | B1 OutputGate 誤判年份／代號 | MUST | ✅ | OutputGate_ExcludesContextNumbersButKeepsFinancialClaims：3 個正案例先失敗 | 5 個正反案例、拒答事件與 artifact、UI 新舊圖及原因顯示 | 本項提交 | 不忽略代號同值的財務金額 |
| R17 | B3 情境偷換＋澄清 | MUST | ✅ | 轉入既有持股原本 scenario_invalid | 守恆／其他持股／錯誤目標／替換拒絕／回覆取保存值 | 本項提交 | 自然語言路由澄清於 R34 再驗收 |
| R18 | B2 優化建議 | MUST | ✅ | 無情境的優化建議原本仍 passed | 提交 Guard、足夠／不足資料 gate、四指標與差異取同快照 | 本項提交 | |
| R19 | B4 路由重試＋log | MUST | ✅ | | | | |
| R20 | B8 拖累正負號 | SHOULD | ⬜ | | | | |
| R21 | B6 VaR 樣本不足 | SHOULD | ⬜ | | | | |
| R22 | B5 數字口徑 | SHOULD | ⬜ | | | | |
| R23 | B7 產業分類 | SHOULD | ⬜ | | | | |
| R24 | B9、B10 前端 | COULD | ⬜ | | | | |
| R25 | B11 log 等級 | COULD | ⬜ | | | | |

### 驗證

| # | 驗收條件 | 優先 | 狀態 | 證明 | Commit | 備註 |
|---|---|---|---|---|---|---|
| R26 | 8 題 eval 不退步 | SHOULD | ⬜ | | | |
| R27 | 全部驗證全綠 | 每次 commit | ✅ | 補充規格同步時完整驗證通過，見下表 | 本項提交 | 每項重新執行 |

### 補充規格：角色、方法、來源與路由

| # | 驗收條件（摘要） | 優先 | 狀態 | 證明 | Commit | 備註 |
|---|---|---|---|---|---|---|
| R28 | 共用基礎＋角色＋本角色 skill 目錄＋已載入全文；唯讀 load_skill、至少 3 個 skill、載入上限與可追溯紀錄 | MUST | ✅ | 角色隔離／下一輪全文／批次上限／不可擴權／雜湊／設定擴充／trajectory | 本項提交 | 與 R9 一併做；保留法說會原 prompt，刪 Supporting skill；不能擴大權限；記錄 id／版本／雜湊及 trajectory |
| R29 | Router 只選工作流與參數，不選 skill／capability | MUST | ✅ | | | 與 R9 一併做；取代先前 Router 選 playbook 的方案 |
| R30 | 投組任務型工具 ≤ 8；37 個數學函式不可直接呼叫；重用情境計算 | MUST | ✅ | 六工具白名單、全數底層函式拒絕、各工具數值注入拒絕、快照／所有權情境測試 | 本項提交 | R5–R7 完成前先驗收；load_skill 不計入 8 個上限 |
| R31 | 研究角色來源規則與可確定性檢查的 OutputGate | MUST | ✅ | prompt／證據中繼資料、6 組來源錯置正反例 | 本項提交 | R16 後優先補做；直接／補充來源不可錯置，表格／目錄／封面不得充當風險或實質證據 |
| R32 | 台積電／鴻海風險、法說會新題庫與原始 PDF 核實紀錄；validate 通過 | SHOULD | ⬜ | | | R26 前；至少各 1 題，Garage 原始 PDF 核對文件、實體頁碼、摘錄前保持 verified: false；與原 8 題分開計分 |
| R33 | 新題庫 investigation、skill trajectory、拆解實驗 A（MaxTurns=1）與 C | COULD | ⬜ | | | R32 核實後、R26 前或一起做；沿用累計 US$10 預算，無法確認剩餘額度先記錄阻塞 |
| R34 | 入口／追問免 LLM 路由、程式訊號、權限與參數驗證、低信心及不支援情境澄清 | MUST | ✅ | | | 與 R9 一併做；每種情況均有測試，不猜投組或替換情境；混合問題單 run 不跨角色 |
| R35 | 合併對話與 Router 為一次 LLM 呼叫，輸出 D14 結構 | SHOULD | ✅ | | | 與 R9 一併做；保護直接回覆、澄清、拒絕、重置、修訂上次 run；明確入口仍為零次路由呼叫 |
| R36 | 約 20–30 句 Router 題庫，程式評分與準確率／混淆矩陣 | SHOULD | ⬜ | | | R26 前或一起做；涵蓋研究、投組、混合、模糊、越權、無投組與超出情境能力；不用 LLM judge |

## 更新後執行順序

已完成 R2、R3、R4、R16；R1 防護基礎完成，待移除舊註冊後總驗收。R5–R7 已完成並提交。

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

## 刪除清單

| 檔案 | 行數 | 理由 |
|---|---|---|
| `src/EquityLens.Api/Services/Agents/AgentDynamicPlanCoordinator.cs` | 181 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/AgentLoopController.cs` | 256 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/ClaimSetValidation.cs` | 105 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/DraftRevisionAgent.cs` | 180 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/DraftRevisionNodeHandlers.cs` | 220 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/DynamicPlanValidation.cs` | 346 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/DynamicWorkflowPlanning.cs` | 492 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/EvidenceAssessor.cs` | 92 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/EvidenceReanalysisWorkflow.cs` | 234 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/EvidenceRemediationAgent.cs` | 188 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/EvidenceRemediationNodeHandlers.cs` | 430 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/EvidenceRemediationWorkflowDefinitionProvider.cs` | 51 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/EvidenceRetrievalPlanAgent.cs` | 73 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/FeedbackRevisionWorkflow.cs` | 154 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/PlannerInvocation.cs` | 90 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/PortfolioDiagnosisNarrativeAgent.cs` | 78 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/ResearchInvestigationWorkflow.cs` | 474 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Api/Services/Agents/SourcePolicyRules.cs` | 35 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Web/src/services/agentRegistryAdmin.ts` | 39 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Web/src/views/admin/AgentRegistryView.vue` | 217 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `src/EquityLens.Web/src/views/admin/__tests__/AgentRegistryView.test.ts` | 129 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/AgentRunExecutorLoopPlanningTests.cs` | 141 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/DynamicWorkflowPlanningTests.cs` | 406 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/EvidenceAssessorTests.cs` | 154 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/EvidenceReanalysisWorkflowTests.cs` | 111 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/EvidenceRemediationWorkflowTests.cs` | 445 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/EvidenceRetrievalPlanAgentTests.cs` | 47 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/LlmDraftRevisionAgentTests.cs` | 123 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/PlannerInvocationTests.cs` | 223 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/PortfolioDiagnosisLoopTests.cs` | 141 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/ResearchQualityReviewLoopPolicyTests.cs` | 115 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |
| `tests/EquityLens.Api.Tests/Services/Agents/SourcePolicyRulesTests.cs` | 76 | 已刪機制／入口及其專用測試；有效行為移至固定圖與工具測試 |

## 收斂前後對照（R15）

| 指標 | 收斂前 | 收斂後 |
|---|---|---|
| 可建立的工作流 | 9 | 3 |
| catalog node type | 54 | 13 |
| `Services/Agents` 行數 | 11,674 | 7,550（R10 提交點） |
| 編排模式 | Static／Stateful／DynamicStateful | 固定 DAG＋節點內 Agent loop |

## 阻塞

| 項目 | 原因 | 已嘗試 | 建議 |
|---|---|---|---|
| | | | |

## 給 Reviewer 的摘要

（結束前填寫：完成了什麼、沒完成什麼、收斂前後數字、已知風險、正式站問題處理結果、建議的 merge 與部署步驟）

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
