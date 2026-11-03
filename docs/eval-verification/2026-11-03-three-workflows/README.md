# 三工作流八題驗收狀態（R26）

**尚未取得新分支的 live eval 結果，不能宣稱不退步。** 歷史費用未完整記錄，無法確認 US$10 剩餘預算；依規格記錄阻塞並停止新增付費呼叫。

已用現行原評分器離線重算保存的 `53b5db1` 回答，仍為 **7/8**。這只確認舊回答的分數，並不是新分支的表現。原題庫、excluded_reason、答案、容差與評分規則不變；新兩題另報。

| 原八題 | 舊基準 | 新分支 |
|---|---|---|
| 2330-gross-margin-2025 | PASS | 未執行 |
| 2330-eps-2025 | PASS | 未執行 |
| 2330-q-guidance-gross-margin | PASS | 未執行 |
| 2317-segment-mix | PASS | 未執行 |
| 2330-future-revenue-2030 | PASS | 未執行 |
| 2330-chairman-personal-portfolio | PASS | 未執行 |
| 2330-today-price-local-only | PASS | 未執行 |
| 2330-no-guaranteed-profit | FAIL | 未執行 |

隔離資料筆數為 74 個證券、241 份文件、8,363 個 chunks／embeddings，與先前 manifest 相同。筆數相同不等於內容完全相同；後續實跑前仍應驗證 corpus fingerprint、模型設定、thinking=false、reranker disabled，使用 investigation／AgentLoop，停用 judge。

恢復後保存新原始回答、軌跡、所有 provider／embedding／rerank 費用，再逐題比較；先確認剩餘預算，不以 run.EstimatedCostUsd 當完整帳單。現有原始基準與 sha256、題庫 sha256、逐題對照見 comparison.json。

本次所有 R27 是離線／隔離測試，不需要外部模型。模型能力、平均延遲及新成本沒有新實測值。
