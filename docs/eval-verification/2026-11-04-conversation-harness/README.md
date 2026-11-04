# Conversation harness 真實模型 smoke test

2026-11-04，以真實 GPT-6 Luna（medium reasoning）執行 journey-00 的三輪對話：「我有幾個投資組合？」→「組合甲」→「這個波動率如何？」。被測 API build：`921c8ce878934862e4ff6e30ad72a9a0f8f6f363`。這是整合 smoke test，不是全題庫或 LLM judge 驗收。

## 環境與方法

- 每次建立新的 `_eval` 資料庫、獨立 Redis 與 localhost API，透過實際 migration、註冊 API 和 seed CLI 建立 standard fixture；未使用正式站帳號或投組。
- 價格與投組為固定合成資料；benchmark 由本地 fixture 提供。聊天經真實 SSE、Router、Agent、DB 工具、Output Gate 與審閱 API；模型由 OpenAI Responses API 提供。
- 模型代理限制為 `gpt-6-luna`、一般服務層級、短 context、function tools 與有上限的輸出。其他搜尋／reranker 金鑰關閉；本題不需要研究語料或 embedding。
- 兩次合計硬預算 US$0.50。Ledger 使用 input US$0.225/M、output US$0.50/M 的保守上界，沒有扣除 cache 優惠；參照 [官方模型價格](https://developers.openai.com/api/docs/models/gpt-6-luna)。實際帳單以 provider 為準。

## 發現與結果

| 嘗試 | 結果 | 模型呼叫 | 成本上界 USD |
| --- | --- | --- | --- |
| 1 | t3 因 harness 的 `unsupported_approval` 中止 | 5 | 0.00420930 |
| 2 | 三輪完成、Run Succeeded；11 pass / 0 fail / 1 unverifiable | 5 | 0.00430515 |

第一次發現 runner 錯把審閱節點 key 寫成 `approve`，實際後端常數與 API 都是 `approvePortfolioDiagnosis`。修正精確比對並更新 mock 契約；保留 WorkflowGate、ReadOnly、工作流與節點型別檢查，新增未知節點拒絕的測試。離線測試共 **187 passed**。

第二次在 t2 選定組合甲後，t3 直接建立 PortfolioDiagnosis，Run input／黑板／成功工具的投組 ID 一致；所有權、Session 延續、trace、持倉不變檢查通過。工具包含 snapshot、saved risk report、risk analysis 與 risk profile。Output Gate 為 `passed: true, limited: true`，沒有 blocking issues 或 rejected numbers。唯讀審閱由 Pending 進入 Approved，Run 最終 Succeeded。

`owned_portfolio_list` 保留 unverifiable：第一輪沒有結構化的已列出投組 ID。案例仍是 draft，完整 conversation success 為 N/A；不能將 exit 0 或 11/11 已可驗證項目通過說成完整對話成功率 100%。相同真實輸出以 `conversation rescore` 離線重評，summary 完全一致。

## 證據與限制

- `verification.json`：逐輪回答摘要、模型 token、節點／工具狀態、來源 metadata 與原始 artifacts SHA256。第二次 harness 尚未 commit，因此 metadata 如實保留 dirty 與來源 hash。
- `attempt-*-summary.json`：兩次完整 scorer 報告。
- `attempt-*-provider-usage.jsonl`：兩次 provider 用量與預算結算，共 US$0.00851445，無未結算預留。
- 完整 responses、fixture、checkpoint 與私有 seed manifest 保留於本機 `/tmp/equitylens-journey-live-20261104-r{1,2}/`，未提交帳密或 API key；此摘要不是可獨立 rescore 的完整輸出包。
- 尚未執行其餘 10 組真實對話；需要核實的研究語料。尚未加入分類型 LLM judge，因此沒有測試 judge 的評分品質或校準。

測試結束已停止專用 API、proxy、benchmark、Redis；隔離資料庫留供診斷。
