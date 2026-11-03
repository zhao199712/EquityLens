# 收斂為 3 個工作流 — 規格補充（2026-11-03）

> 本文件補充 [`remove-dynamic-dag.md`](remove-dynamic-dag.md)，規則相同：只寫目標、決策、邊界與驗收；怎麼做由實作者決定，偏離時記在主規格 §9。
> 主規格的 §3 決策、§5 邊界照舊有效。本文件的項目編號接續主規格（D9 起、R28 起）。
> 進度請在 `remove-dynamic-dag-progress.md` 新增對應列。

## 背景

1. A/B 報告（`docs/eval-verification/2026-11-02-agent-loop-ab/README.md`「改善原因與成本分析」）發現：v4 迴圈的 system prompt **沒有** v1 起草 prompt 的來源角色規則（Primary／Supporting 區分；風險題不可把年報揭露說成法說會提到）。目前 8 題計分題沒有風險或法說會題，**無法看出是否退步**，而正式站已預設使用 v4。
2. 主規格對 skill、router、工具粒度的描述太模糊，可能導致法說會 prompt 被誤刪，或把 37 個底層數學函式直接開放給 LLM。

## 新增決策

| # | 對象 | 決策 |
|---|---|---|
| D9 | **Skill → Playbook** | `WorkflowSkillCatalog` 的 4 個 Lead skill 改為 **playbook**：一份給 Agent 迴圈的設定 = 研究方法 prompt ＋ 可用工具範圍 ＋ 預算預設 ＋ 必要輸入。至少包含：一般個股研究、法說會要點（**沿用現有 `InvestmentResearchSkillPrompts.ConferenceCallTakeaways`**）、市場事件（Web 優先，仍受 source policy 限制）、投組風險／優化。7 個 Supporting skill（只是 capability 打包）刪除。 |
| D10 | **Router** | `InvestmentResearchRouter` 的輸出改為「工作流（Research／Portfolio）＋ playbook ＋ 參數（ticker、投組、情境）」，不再帶 capability 或 skill 清單。解析失敗依主規格 B4 處理（重試一次、仍失敗回澄清、記 warning log）。 |
| D11 | **工具粒度** | 給 LLM 的工具以**任務為單位**：研究迴圈維持約 3 個（`search_local`、`search_web`、`get_document_page`）；投組迴圈 **≤ 8 個**，建議形狀：持股快照、讀取既有風險報告、補算指定類型的風險分析（類型為 enum）、績效歸因、權重情境模擬、風險偏好。`PortfolioRiskMathCapabilities` 的 37 個底層函式**不得直接作為工具暴露**，只能在工具內部使用。既有的 `evaluate_portfolio_scenario` 與 `EvaluatePortfolioScenarios` 節點（已部署）納入投組迴圈的情境工具，不要另做一套。 |
| D12 | **來源角色規則** | 研究迴圈的 prompt（或 playbook）補回 v1 的來源角色規則：區分直接來源與補充來源；不可把補充來源的事實歸給直接來源；風險題不可把年報揭露說成法說會提到；財報表格頁不可當作風險證據，目錄頁與封面不可當作實質證據。若 OutputGate 能以確定性方式檢查其中一部分（例如引用頁的文件類型與題目意圖不符），一併實作。 |
| D13 | **風險與法說會 eval 題** | 新增 **2330／2317 範圍內**的風險題與法說會題（至少各 1 題）。**標準答案必須對照原始 PDF 核實**（記錄文件、實體頁碼、摘錄），核實前保持 `verified: false`，不可憑模型記憶或搜尋摘要填答案。放在新的題庫檔（例如 `evals/cases/research_risk_conference.jsonl`），不併入既有 8 題的 pass_rate。 |

## 驗收清單

| # | 驗收條件 | 優先 |
|---|---|---|
| R28 | D9：playbook 機制完成，至少 4 個；法說會 playbook 使用原有 prompt；Supporting skill 不再存在；新增 playbook 只需新增設定（測試證明） | MUST |
| R29 | D10：router 輸出工作流＋playbook＋參數；不再引用 capability／skill 清單；Ask Agent 正常運作 | MUST |
| R30 | D11：投組工具 ≤ 8 個；測試證明 37 個底層函式無法被 LLM 直接呼叫；既有情境工具被重用 | MUST |
| R31 | D12：來源角色規則補回研究迴圈；有測試證明 prompt／playbook 含這些規則；可確定性檢查的部分有 OutputGate 測試 | MUST |
| R32 | D13：新題庫檔與核實紀錄（`docs/eval-verification/<日期>-risk-conference-cases/`）；`validate` 通過 | SHOULD |
| R33 | 若環境可用：用新題庫跑 `--mode investigation`，記錄結果；並依 A/B 報告「建議的拆解實驗」執行實驗 A（`MaxTurns=1`）與 C，結果寫入報告 | COULD |

## 建議插入的執行順序

主規格順序中，在 **R5–R7（投組迴圈）之前**先做 R30（決定工具形狀），在 **R9（Ask Agent）時**一併做 R28、R29；R31 在 R16（OutputGate 誤判）之後做；R32、R33 放在 R26（eval）之前或一起做。
