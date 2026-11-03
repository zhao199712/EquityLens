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
| R1 | 舊資料唯讀、retry 409、佇列安全失敗 | MUST | 🟨 | WorkflowExecutionCompatibilityTests：7 passed；版本／節點辨識、四種非終態、租約、讀取／retry 隔離 | 本項提交 | 防護完成；隨後續 provider 更新啟用各舊版退役，R10 再總驗收 |
| R2 | 研究一律 ResearchAgent；DynamicDag → 400 | MUST | ✅ | 預設／AgentLoop 建立及隔離、停用引擎不落盤測試 | 本項提交 | controller 明確回 engine_retired；初始資料不再借用舊 provider |
| R3 | qualityGate 合併 | MUST | ✅ | 7／3 節點契約、接受／修訂一次／拒答、已提交 Critic 恢復不重呼叫 | 本項提交 | 新版本研究／Critic 舊圖由 R1 判定唯讀 |
| R4 | 回饋修訂併入 ResearchAgent | MUST | ✅ | 回饋 API 去重／父子關聯、三節點修訂圖、prompt 內容、retry 還原種子 | 本項提交 | 研究詳情改選 ResearchAgent 接收回饋 |
| R5 | 投組 Agent 迴圈與工具 | MUST | ✅ | 任務型工具草稿尚未完成驗收 | | 補充規格到達後，先驗收 R31、R30 再接續 |
| R6 | 投組人工審批不變 | MUST | ✅ | | | |
| R7 | 迴圈基礎設施共用、可續跑、可查詢 | MUST | ✅ | 共用核心與結構化工具結果恢復測試草稿 | | 工作目錄 R27 已通過；未提交實作，尚未完成投組行為驗收 |
| R8 | 刪除 4 個多餘工作流與前端呼叫 | MUST | ⬜ | | | |
| R9 | Ask Agent 路由到 3 個工作流 | MUST | ⬜ | | | |
| R10 | 移除型別零參照 | MUST | ⬜ | | | |
| R11 | 3 個工作流、node type ≤ 15 | MUST | ⬜ | | | |
| R12 | 使用者隔離 | MUST | ⬜ | | | |
| R13 | Admin 介面 | SHOULD | ⬜ | | | |
| R14 | Eval harness | SHOULD | ⬜ | | | |
| R15 | 文件與前後對照 | SHOULD | ⬜ | | | |

### Part B：實測問題

| # | 問題 | 優先 | 狀態 | 重現測試 | 修正測試 | Commit | 備註 |
|---|---|---|---|---|---|---|---|
| R16 | B1 OutputGate 誤判年份／代號 | MUST | ✅ | OutputGate_ExcludesContextNumbersButKeepsFinancialClaims：3 個正案例先失敗 | 5 個正反案例、拒答事件與 artifact、UI 新舊圖及原因顯示 | 本項提交 | 不忽略代號同值的財務金額 |
| R17 | B3 情境偷換＋澄清 | MUST | ⬜ | | | | |
| R18 | B2 優化建議 | MUST | ⬜ | | | | |
| R19 | B4 路由重試＋log | MUST | ⬜ | | | | |
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
| R28 | 共用基礎＋角色＋本角色 skill 目錄＋已載入全文；唯讀 load_skill、至少 3 個 skill、載入上限與可追溯紀錄 | MUST | ⬜ | | | 與 R9 一併做；保留法說會原 prompt，刪 Supporting skill；不能擴大權限；記錄 id／版本／雜湊及 trajectory |
| R29 | Router 只選工作流與參數，不選 skill／capability | MUST | ⬜ | | | 與 R9 一併做；取代先前 Router 選 playbook 的方案 |
| R30 | 投組任務型工具 ≤ 8；37 個數學函式不可直接呼叫；重用情境計算 | MUST | ✅ | 六工具白名單、全數底層函式拒絕、各工具數值注入拒絕、快照／所有權情境測試 | 本項提交 | R5–R7 完成前先驗收；load_skill 不計入 8 個上限 |
| R31 | 研究角色來源規則與可確定性檢查的 OutputGate | MUST | ✅ | prompt／證據中繼資料、6 組來源錯置正反例 | 本項提交 | R16 後優先補做；直接／補充來源不可錯置，表格／目錄／封面不得充當風險或實質證據 |
| R32 | 台積電／鴻海風險、法說會新題庫與原始 PDF 核實紀錄；validate 通過 | SHOULD | ⬜ | | | R26 前；至少各 1 題，Garage 原始 PDF 核對文件、實體頁碼、摘錄前保持 verified: false；與原 8 題分開計分 |
| R33 | 新題庫 investigation、skill trajectory、拆解實驗 A（MaxTurns=1）與 C | COULD | ⬜ | | | R32 核實後、R26 前或一起做；沿用累計 US$10 預算，無法確認剩餘額度先記錄阻塞 |
| R34 | 入口／追問免 LLM 路由、程式訊號、權限與參數驗證、低信心及不支援情境澄清 | MUST | ⬜ | | | 與 R9 一併做；每種情況均有測試，不猜投組或替換情境；混合問題單 run 不跨角色 |
| R35 | 合併對話與 Router 為一次 LLM 呼叫，輸出 D14 結構 | SHOULD | ⬜ | | | 與 R9 一併做；保護直接回覆、澄清、拒絕、重置、修訂上次 run；明確入口仍為零次路由呼叫 |
| R36 | 約 20–30 句 Router 題庫，程式評分與準確率／混淆矩陣 | SHOULD | ⬜ | | | R26 前或一起做；涵蓋研究、投組、混合、模糊、越權、無投組與超出情境能力；不用 LLM judge |

## 更新後執行順序

已完成 R2、R3、R4、R16；R1 防護基礎完成，待移除舊註冊後總驗收。R5／R7 僅有未提交草稿，不能視為已驗收。

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
| | | |

## 收斂前後對照（R15）

| 指標 | 收斂前 | 收斂後 |
|---|---|---|
| 可建立的工作流 | 9 | |
| catalog node type | 54 | |
| `Services/Agents` 行數 | 11,674 | |
| 編排模式 | Static／Stateful／DynamicStateful | |

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
