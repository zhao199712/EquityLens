# 收斂為 3 個工作流 — 規格補充（2026-11-03）

> 本文件補充 [`remove-dynamic-dag.md`](remove-dynamic-dag.md)，規則相同：只寫目標、決策、邊界與驗收；怎麼做由實作者決定，偏離時記在主規格 §9。
> 主規格的 §3 決策、§5 邊界照舊有效。本文件的項目編號接續主規格（D9 起、R28 起）。
> 進度請在 `remove-dynamic-dag-progress.md` 新增對應列。

## 背景

1. A/B 報告（`docs/eval-verification/2026-11-02-agent-loop-ab/README.md`「改善原因與成本分析」）發現：v4 迴圈的 system prompt **沒有** v1 起草 prompt 的來源角色規則（Primary／Supporting 區分；風險題不可把年報揭露說成法說會提到）。目前 8 題計分題沒有風險或法說會題，**無法看出是否退步**，而正式站已預設使用 v4。
2. 主規格對 skill、router、工具粒度的描述太模糊，可能導致法說會 prompt 被誤刪，或把 37 個底層數學函式直接開放給 LLM。
3. 研究與投組是不同角色（工具、邊界、審批都不同），不能共用同一個 prompt；但同一角色內的分析方法應由 Agent 視需要載入，而不是由 router 預先決定。

## 新增決策

| # | 對象 | 決策 |
|---|---|---|
| D9 | **角色與方法分層：Skill 由 Agent 自行載入** | 分兩層，決定者不同：<br>**① 角色（工作流）**：研究 Agent／投組 Agent，由 Router 在開始前決定，**執行中不可切換**（工具、安全邊界、審批流程不同）。<br>**② 方法（skill）**：由 Agent 在迴圈中透過唯讀工具 `load_skill` **自行決定是否載入**（progressive disclosure）。<br>Prompt 組成：**共用基礎**（JSON 動作協定、文件為不可信資料、不編造、預算）＋ **角色 prompt**（依工作流固定）＋ **skill 目錄**（只有 id、名稱、一句說明）＋ **已載入的 skill 全文**。<br>現有 4 個 Lead skill 轉為 skill 檔：一般個股研究的方法可併入研究角色 prompt；**法說會要點沿用 `InvestmentResearchSkillPrompts.ConferenceCallTakeaways`**；市場事件；投組風險／降波動優化。7 個 Supporting skill（只是 capability 打包）刪除。Skill 內容以檔案或設定保存，新增方法只需新增一個 skill，不改程式與 router。<br>**Skill 規則**：(a) skill 只能增加說明，**不能擴大工具可見性或權限**，最多可縮小範圍；(b) 每個 skill 只屬於一個角色，另一個角色看不到；(c) `load_skill` 與其他工具一樣寫入 `AgentToolCall`，記錄 skill id、版本與內容雜湊；(d) 每個 run 最多載入 3 個 skill（Options 可調），超過由 Guard 拒絕並記錄；(e) 軌跡（eval trajectory）記錄載入了哪些 skill。 |
| D10 | **Router** | `InvestmentResearchRouter` 只決定**角色（工作流：Research／Portfolio）＋ 參數**（ticker、投組、情境、期間），不選 skill，也不帶 capability／skill 清單。解析失敗依主規格 B4 處理（重試一次、仍失敗回澄清、記 warning log）。 |
| D11 | **工具粒度** | 給 LLM 的工具以**任務為單位**：研究迴圈維持約 3 個（`search_local`、`search_web`、`get_document_page`）＋ `load_skill`；投組迴圈 **≤ 8 個**，建議形狀：持股快照、讀取既有風險報告、補算指定類型的風險分析（類型為 enum）、績效歸因、權重情境模擬、風險偏好，再加 `load_skill`（不計入 8 個上限）。`PortfolioRiskMathCapabilities` 的 37 個底層函式**不得直接作為工具暴露**，只能在工具內部使用。既有的 `evaluate_portfolio_scenario` 與 `EvaluatePortfolioScenarios` 節點（已部署）納入投組迴圈的情境工具，不要另做一套。 |
| D12 | **來源角色規則** | 研究角色 prompt 補回 v1 的來源角色規則：區分直接來源與補充來源；不可把補充來源的事實歸給直接來源；風險題不可把年報揭露說成法說會提到；財報表格頁不可當作風險證據，目錄頁與封面不可當作實質證據。若 OutputGate 能以確定性方式檢查其中一部分（例如引用頁的文件類型與題目意圖不符），一併實作。 |
| D13 | **風險與法說會 eval 題** | 新增 **2330／2317 範圍內**的風險題與法說會題（至少各 1 題）。**標準答案必須對照原始 PDF 核實**（記錄文件、實體頁碼、摘錄），核實前保持 `verified: false`，不可憑模型記憶或搜尋摘要填答案。放在新的題庫檔（例如 `evals/cases/research_risk_conference.jsonl`），不併入既有 8 題的 pass_rate。 |
| D14 | **路由決策分層** | Router 只回答「研究或投組」，依序：<br>**第 0 層 入口決定**：研究頁 → Research；投組頁診斷 → Portfolio；既有對話追問 → 沿用上一個 run 的角色。這些情況**不呼叫 LLM 路由**。<br>**第 1 層 程式訊號**：使用者沒有投組 → 不可走 Portfolio；訊息含使用者投組名稱 → 強烈偏向 Portfolio；股票查詢比對到的 ticker 作為參數候選提供給 LLM。<br>**第 2 層 LLM 分類**（只處理模糊情況）：結構化輸出 `{ action: route\|clarify\|direct_reply\|reject, workflow, params{ticker, portfolioId, scenarios, period}, missing[], confidence }`。判斷標準：主詞是「我的持股／配置」、問風險與配置、要採取行動 → Portfolio；主詞是公司、問財報／法說會／新聞、要了解事實 → Research。<br>**第 3 層 程式驗證**（不得跳過）：多個投組且未指明 → 澄清並列出選項；ticker 查不到 → 澄清；投組 ID 不屬於該使用者 → 拒絕並記錄；`confidence` 為 low → 澄清；情境超出工具能力 → 澄清，**不得改用其他情境代替**（B3）；解析失敗 → 重試一次後澄清並記 warning（B4）。原則：寧可多問一次，不猜角色。<br>**混合問題**（同時涉及財報與持股調整）：依主要意圖選一個角色作答，並在回覆結尾提議另開另一個角色的 run；**不可**讓單一 run 跨角色使用另一角色的工具。 |

## 驗收清單

| # | 驗收條件 | 優先 |
|---|---|---|
| R28 | D9：prompt 依「共用基礎＋角色＋skill 目錄＋已載入 skill」組成；`load_skill` 工具完成；至少 3 個 skill 檔（法說會要點使用原有 prompt、市場事件、投組降波動優化）；Supporting skill 不再存在。測試證明：skill 目錄只列本角色的 skill；載入後全文出現在下一輪 prompt；skill 無法讓不可見的工具變可見；超過載入上限被拒絕並記錄；`AgentToolCall` 有 skill id／版本／雜湊；新增 skill 不需改程式 | MUST |
| R29 | D10：router 只輸出工作流＋參數；不引用 capability／skill 清單；Ask Agent 正常運作 | MUST |
| R30 | D11：投組工具 ≤ 8 個；測試證明 37 個底層函式無法被 LLM 直接呼叫；既有情境工具被重用 | MUST |
| R31 | D12：來源角色規則補回研究角色 prompt；有測試證明 prompt 含這些規則；可確定性檢查的部分有 OutputGate 測試 | MUST |
| R32 | D13：新題庫檔與核實紀錄（`docs/eval-verification/<日期>-risk-conference-cases/`）；`validate` 通過 | SHOULD |
| R33 | 若環境可用：用新題庫跑（trajectory 需含載入的 skill） `--mode investigation`，記錄結果；並依 A/B 報告「建議的拆解實驗」執行實驗 A（`MaxTurns=1`）與 C，結果寫入報告 | COULD |
| R34 | D14 第 0、1、3 層：入口與追問不呼叫 LLM 路由；沒有投組時不可走 Portfolio；投組 ID 越權被拒並記錄；多投組未指明、ticker 查不到、低信心、超出工具能力的情境皆回澄清；每一種情況都有測試 | MUST |
| R35 | 將 `LlmConversationAgent` 與 `InvestmentResearchRouter` 合併為**一次** LLM 呼叫，直接輸出 D14 的結構；既有對話行為（直接回覆、澄清、拒絕、重置、修訂上一次 run）不退步 | SHOULD |
| R36 | Router eval：新題庫（例如 `evals/cases/router_v1.jsonl`）約 20～30 句使用者訊息，每句標註預期 action／workflow／關鍵參數，涵蓋研究、投組、混合、模糊、越權、無投組、超出情境能力；全部以程式評分（不用 LLM judge），輸出準確率與混淆矩陣；結果存 `docs/eval-verification/<日期>-router/` | SHOULD |

## 建議插入的執行順序

主規格順序中，在 **R5–R7（投組迴圈）之前**先做 R30（決定工具形狀），在 **R9（Ask Agent）時**一併做 R28、R29、R34、R35；R31 在 R16（OutputGate 誤判）之後做；R32、R33 放在 R26（eval）之前或一起做。
R36 放在 R26（eval）之前或一起做。
