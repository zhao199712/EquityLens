# Ask Agent 情境式追問

本功能只在 Ask Agent 最新回覆下方提供可編輯的追問草稿。

## 行為與契約

既有 `ConversationRunCardDto` 新增可選的 `suggestedActions`；舊 API 缺少欄位或回傳 null 時，前端視為空陣列。每個選項包含穩定 `id`、繁中 `label`／`prompt`，以及英文 `labelEn`／`promptEn`。英文缺少時回退至原文字。最多三個；模型可以選一至三個。

選項採混合方式：`ConversationSuggestedActionProvider` 先依已保存的結構化資料產生全部有效候選，`ConversationSuggestedActionRanker` 再依原問題和已驗證的答案挑選排序。模型只能回傳核准候選的 ID，不能改寫中英文標籤／問題，也不能呼叫工具。無效 JSON、未知 ID、重複 ID、額外欄位、超過三項、供應商錯誤或逾時均回退到規則順序的前三項。

排序在研究／投組既有 finalize 節點內執行，每份新報告最多一次呼叫，20 秒逾時、最多 1024 output tokens。呼叫前先經由現有 lease fence 保存回退 ID；若中斷，恢復時沿用回退結果，不重複呼叫。成功排序保存於 blackboard 的 `conversationSuggestedActionRanking`，模型與 token 用量保存為 `SuggestedActions` 模式的 `AgentLlmTurn`。成本沿用系統設定的估價費率，不代表供應商實際帳單。

讀取卡片完全不呼叫模型或寫入 DB，會重新驗證規則、權限與最新訊息條件，再套用保存的 ID。既有未保存排序的報告採規則順序。未新增 endpoint、資料表、migration、節點或 workflow。

| 情境 | 選項 | 依據 |
| --- | --- | --- |
| 投組分析 | 降波動、風險貢獻、壓力測試、大盤比較、尾端風險、績效歸因、偏好適配 | 依已保存工具結果排除已涵蓋的主題；只有壓力／歸因成功時也可提供候選，但沒有波動數值時不推薦降波動 |
| 已完成情境比較 | 剩餘風險、指標解釋 | Calculated 的比較、同投組快照、baseline 與各情境數值，且無 scenarioErrors |
| 單一公司研究 | 營運風險、結論依據 | 已接受的 Output Gate／Critic、有引用、本人研究紀錄及唯一有效公司代號 |

投組名稱從本人目前有效的投組資料讀取，並以既有 `PortfolioSelection` 再次確認中英文完整問題都能唯一辨識。無權限、停用、同名或其他文字歧義時不提供。第一版不推薦指定 ETF。

## 顯示與生命週期

- Run 必須 Succeeded，且通過 `WorkflowExecutionCompatibility`。等待核准、執行中、失敗、退役圖、缺少必要資料均無選項。
- 投組要求 gate.passed=true、limited=false、無 blockingIssues、evidenceStatus=complete；研究要求 Answered、Output Gate passed、Critic AcceptAnswer，拒答及降級摘要不顯示。
- 後端確認此 Run 屬於 session 的最新訊息，且仍為 context.lastAgentRunId。Reset 或新的訊息使舊回答失去選項；重新讀取歷史也不會恢復。仍有效的最新回答可重新載入。
- 前端只呈現最新助理訊息；傳送中或載入另一個對話時不顯示。
- 點擊只填入輸入框並聚焦。已有任何草稿（含空白）時停用選項；不覆蓋、不 POST、不建立 Run、不更新 session。使用者可改寫後自行送出，保留 `isSubmitEnter` 輸入法防護。
- 正式送出仍走原 conversation router、標的選擇、權限及工作流檢查。新 Run 仍需通過原有 Quality Gate；PortfolioDiagnosis v5 為唯讀分析，沒有人工審批節點。
- Session 切換／清除以 viewVersion 隔離載入、串流與輪詢的晚到回應；已完成串流忽略後續事件。舊 Run 進度仍可更新，但不恢復舊選項。
- prestige 按鈕使用原生鍵盤操作、focus-visible 及可換行排版；Run 標題在手機換行避免溢出。

## 驗證

2026-11-04 本機驗證：

- 後端 Release：1,188 passed、0 failed、0 skipped；TRX guard 確認其中 18 個真實 PostgreSQL 整合測試。使用本次獨立 ParadeDB 容器，非正式或其他工作樹的服務。
- 追問與既有 conversation 測試共 46 項；涵蓋結果去重、品質與退役抑制、本人隔離、Reset／歷史重讀。
- 建立 Run 的服務整合測試使用正式 ConversationService、AgentWorkflowQueryService、AgentRunService 及記憶體 DB／記錄型 queue。四組中英文／原投組或改寫投組組合，驗證保存的 Run input.portfolioId、擁有者與審批節點；外部投組 ID 被拒絕。未執行 LLM 或量化 worker。
- 前端 Vitest：177 passed，涵蓋只填入、草稿保護、IME、中英文、對話切換、A→B→A 晚到輪詢、Reset 及重複串流事件。
- `npm run test:e2e:ci` 包含 TypeScript、production build 及 Chromium smoke，19/19 passed。新增兩種語言的草稿／POST 次數／IME／重新載入驗證，以及情境比較選項驗證；手機案例檢查水平溢出。
- 瀏覽器 API 為固定測試回應，與上述服務整合測試分開；不宣稱驗證完整 HTTP→模型→報告鏈或真實分析品質。

上述為 2026-11-04 的歷史驗證紀錄；不代表後續混合排序已在該輪驗證。

## 本機模型與混合排序驗證

`launchSettings.json` 的本機 http profile 設定 `AI__ChatProvider=OpenAI`、`OpenAI__Model=gpt-6-luna`。金鑰從私有設定或 `OPENAI_API_KEY` 讀取，不寫入版本庫。使用 `--no-launch-profile` 或直接執行 DLL 時，須明確傳入相同環境變數。

自動化回歸涵蓋：真實 finalize 到卡片讀取、從全部候選挑選、保存後重讀不呼叫模型、不合法 ID／JSON、供應商失敗、取消後恢復、品質及權限抑制、研究答案的接受／拒答分支。點擊仍僅填入草稿，送出才開始下一輪。尾端風險模板明確使用本次分析資料期間、1 日持有期及 95% 信心水準。

2026-11-10 的付費驗證使用隔離 seed DB 的合成投組（台積電 2000 股、鴻海 5000 股），不是使用者的真實持倉。原始結果存於本機私有暫存目錄，不提交 token 或測試帳號憑證。未部署或改動正式服務。

### 本次結果與限制

- 完整後端：1234 passed、0 failed、0 skipped，含 18 個真實 PostgreSQL 整合測試；fixture 在 seed 服務建立獨立 UUID 資料庫，結束後移除。
- 前端：186 passed；本次未改前端互動。真實對話透過 HTTP 送出回傳的追問文字，並非瀏覽器點擊驗證。
- 實際模型：路由、投組迴圈與 `SuggestedActions` 保存紀錄均確認為 `openai / gpt-6-luna`。
- 壓力題 `b1c62fe1`：一次 `run_stress_test`，品質通過；模型選尾端風險與偏好適配。
- 尾端風險 `84114962`：`run_risk_analysis`，品質通過。初次固定模板缺持有期而要求澄清，補上 1 日後完成；模板現已明確化。該次發生於尾端風險候選修正之前，未保存模型排序；修正後的候選路徑有回歸測試，不回填舊 run 的模型結果。
- 大盤比較 `06d0ff74`：`get_performance_attribution`，品質通過；模型選持股風險貢獻與壓力測試。前一輪 `2ae0e66a` 曾因「負貢獻 0.28 個百分點」被誤擋，已修正語意負號並加入錯值／正向說法／缺引用反例。
- 從大盤比較送出風險貢獻追問 `76b9d2ca`：保留 parent run，使用 `run_risk_analysis`；模型選降波動、壓力測試與偏好適配。三次重讀卡片的 LLM turn 數均為 4，選項相同；前後投組與持倉 JSON 完全相同。

**尚未修正的答案語意缺口：** 風險貢獻追問雖通過數字 gate，人工核對發現模型把工具的 `componentVolatility`（台積電約 24.85%、鴻海約 5.80%，屬成分波動貢獻）稱為「個股波動度」。數字可追溯不代表指標語意正確；此案例不算答案品質完全通過。混合追問與本機模型切換已完成，此語意檢查仍需另外修正。


## 多輪脈絡與引用修正（2026-11-10）

- 伺服器保存待完成問題與已解析參數；使用者補投組名稱後繼續原問題。沒有待完成問題時，「接下來看組合甲」只選投組。
- 文字追問重新分類當前目的。「那／接著」不再強制沿用前一工作流。前一 Run 僅在同角色時提供背景；切換公司採用新問題指定的標的。
- 情境追問帶入原 Run 保存的使用者指定方案，以及本人持倉的名稱／代號對照；只改新指定的條件。模型自行提出的方案不會升級成使用者已確認的方案。
- 研究轉投組時，伺服器重新核對研究與來源擁有權、已完成狀態、原品質關卡與數字引用，再建立唯讀證據快照。研究被拒絕時只傳「尚未核實」狀態，不能把拒答當成財報事實。
- Reset 清除待完成任務並設定訊息邊界；重置前的訊息不再送入路由器。
- 原生工具模式保留引用格式指示；明確的全形引用括號統一為證據 ID，未知 ID 仍拒絕。數字關卡容許成對 Markdown 粗體，不放寬算式運算元、結果及同句來源檢查。研究問題年份只在同句引用來源且具有「年」語意時視為主題識別。

回歸測試涵蓋補選投組、單純切換、跨工作流、已確認情境、Reset、研究來源隔離、引用括號、Markdown 算式及年份限制。真實多輪 eval 使用原始題庫訊息、全新合成帳號與隔離服務；未修改題庫或評分器。題庫仍為 draft，程式檢查通過不能替代完整答案語義覆核。


本次完整實測為 11 組／45 輪：37 項工作流選擇、8 項 action、14 項獨立情境重算、11 項擁有權與 11 項持倉不變檢查皆通過；完整評分仍為 159 pass／11 fail／93 unverifiable，8 組有失敗。剩餘問題涉及來源頁要求、財務推導及答案品質，未宣稱全部通過。離線重評與原結果一致。

實測後另補財報年度防護：例如 `2025 年`／`114年` 不得解析成數百年的滾動風險期間，也不覆蓋既有分析期間；極大值先限制範圍，避免整數溢位。此補強由確定性回歸及完整後端測試驗證（1267 passed、0 skipped，含18項真實 PostgreSQL）；沒有把先前45輪結果視為已驗證這個最後補強。前端本次未變更。
