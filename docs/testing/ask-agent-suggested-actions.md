# Ask Agent 情境式追問

基底為 `a0d1477`（已合併 PR #22）。本功能只在 Ask Agent 最新回覆下方提供可編輯的追問草稿。

## 行為與契約

既有 `ConversationRunCardDto` 新增可選的 `suggestedActions`；舊 API 缺少欄位或回傳 null 時，前端視為空陣列。每個選項包含穩定 `id`、繁中 `label`／`prompt`，以及英文 `labelEn`／`promptEn`。英文缺少時回退至原文字。最多三個，目前各情境提供一至兩個。

選項由 `ConversationSuggestedActionProvider` 讀取已保存的結構化資料挑選，無模型呼叫、不解析回答內文、不新增 endpoint、資料表、migration、節點或 workflow。

| 情境 | 選項 | 依據 |
| --- | --- | --- |
| 投組風險概況 | 降低波動率、持股風險貢獻 | 已保存的波動數值；若 mathResults 或先前工具結果已有貢獻資料，省略風險貢獻選項 |
| 已完成情境比較 | 剩餘風險、指標解釋 | Calculated 的比較、同投組快照、baseline 與各情境數值，且無 scenarioErrors |
| 單一公司研究 | 營運風險、結論依據 | 已接受的 Output Gate／Critic、有引用、本人研究紀錄及唯一有效公司代號 |

投組名稱從本人目前有效的投組資料讀取，並以既有 `PortfolioSelection` 再次確認中英文完整問題都能唯一辨識。無權限、停用、同名或其他文字歧義時不提供。第一版不推薦指定 ETF。

## 顯示與生命週期

- Run 必須 Succeeded，且通過 `WorkflowExecutionCompatibility`。等待核准、執行中、失敗、退役圖、缺少必要資料均無選項。
- 投組要求 gate.passed=true、limited=false、無 blockingIssues、evidenceStatus=complete；研究要求 Answered、Output Gate passed、Critic AcceptAnswer，拒答及降級摘要不顯示。
- 後端確認此 Run 屬於 session 的最新訊息，且仍為 context.lastAgentRunId。Reset 或新的訊息使舊回答失去選項；重新讀取歷史也不會恢復。仍有效的最新回答可重新載入。
- 前端只呈現最新助理訊息；傳送中或載入另一個對話時不顯示。
- 點擊只填入輸入框並聚焦。已有任何草稿（含空白）時停用選項；不覆蓋、不 POST、不建立 Run、不更新 session。使用者可改寫後自行送出，保留 `isSubmitEnter` 輸入法防護。
- 正式送出仍走原 conversation router、標的選擇、權限及工作流檢查。新 Run 仍含 Quality Gate 與人工審批節點。
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

未修改多輪 eval harness，未執行付費 eval、部署或正式資料庫變更。
