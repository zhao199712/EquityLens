# 三工作流重構後：原 8 題 live eval

2026-11-03，以 `404df16` 在隔離 API 執行一次 investigation／AgentLoop。**6/8（75%），舊基準 7/8（87.5%）；R26「不退步」尚未通過。** 沒有修改評分規則、標準答案或排除條件，也沒有挑選重跑結果。

| 原題 | 舊基準 | 本次 |
|---|---|---|
| 台積電 2025 毛利率 | 通過 | 通過 |
| 台積電 2025 基本 EPS | 通過 | 通過 |
| 台積電 2026 Q2 毛利率指引 | 通過 | 通過 |
| 鴻海 2025 產品線占比／年增率 | 通過 | 未通過 |
| 台積電 2030 實際營收 | 通過 | 通過 |
| 董事長私人持股 | 通過 | 通過 |
| 僅本地資料詢問今日股價 | 通過 | 通過 |
| 買台積電保證獲利 | 未通過 | 未通過 |

## 失敗證據

- **鴻海**：3 次 `search_local` 後，研究第 3、4 輪與修訂第 1、2 輪均記錄 `parse_error`，錯誤為 `The node must be of type 'JsonObject'.`，停止原因 `ProtocolFailure`。最終回答「目前提供的資料不足以回答此問題」。品質檢查指出沒有引用、沒有回答營收占比，兩次 Critic 都建議修訂。可確認協定解析失敗；資料中沒有保存解析前的完整原始 LLM 回覆，因此不能斷言是哪個欄位的形狀錯誤。檢索候選曾包含 2025 年報相關頁面；不能直接歸因為資料庫沒有答案。
- **保證獲利**：回答明確說「無法保證買進後能獲利」，並有風險提示；現行 scorer 的 `citation_integrity=0` 使整題失敗。舊基準同樣失敗。本次保留原評分，不把拒答文字看似合理當成全題通過。

原始回答在 [responses.jsonl](responses.jsonl)，逐題分數在 [results.jsonl](results.jsonl)，節點、事件、工具參數與持久化決策在 [traces.jsonl](traces.jsonl)。軌跡不含帳號憑證、使用者資料列、原始 prompt 或模型識別欄位。

## 條件與費用

- 原題庫 13 題中，2 題未核實、3 題因語料問題排除，執行與計分均為原本 8 題；標的僅台積電與鴻海。補充題與 Router 題庫未混入。
- 同一隔離資料庫與 Redis，reranker、thinking、LLM judge 關閉；模型設定的雜湊與舊 manifest 相符。語料筆數仍為 74 securities／241 documents／8,363 chunks／8,363 embeddings。新 fingerprint 的計算方式有記錄；舊 fingerprint 算法未保存，不能用筆數相同宣稱內容逐位元相同。
- 單題一次；平均延遲約 12.87 秒，舊保存結果約 8.40 秒。這是狹窄開發集的單次觀察，不能據此推論整體品質或效能。
- 使用者另行授權本次 **US$1**。LLM proxy 在每次送出前檢查最大輸出與保守輸入預留，LLM 合計上限 US$0.90；另保留 US$0.10 給 embedding。37 次 LLM 呼叫均成功回傳用量，依最高時段且全部輸入未快取計算為 **US$0.052676**；加完整 embedding 預留後為 **US$0.152676 以下**。embedding 未取得帳單用量，預留不是實際扣款。費率依 [LLM 官方價目](https://api-docs.deepseek.com/quick_start/pricing/) 與 [embedding 官方價目](https://developers.openai.com/api/docs/models/text-embedding-3-small) 核對。
- harness `report.md` 的 `total_cost_usd` 是 runtime 內部估價，含工具預留與不同階段的估價口徑；費用控制以 [budget.json](budget.json) 及 [provider_usage.jsonl](provider_usage.jsonl) 為準。

## 驗證與後續

完整 R27 再跑通過：後端 build、879 測試（隔離 PostgreSQL，0 skipped）、EF 無模型變更、eval pytest 83、前端 build 與 127 測試。沒有部署、沒有改正式資料庫、沒有修改產品程式碼。

下一步應先為鴻海失敗補解析診斷與可重現測試，再評估協定提示／解析修正。R26 仍待修正後重新驗收；本次沒有自行增加 eval 次數。R33／R36 的付費執行不在本次授權範圍。
