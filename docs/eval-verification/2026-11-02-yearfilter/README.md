# 年度過濾後的 eval（2026-11-02）

接續 [年報重新索引](../2026-11-02-reembed/README.md)（7/11）。追查 `2330-eps-2025` 的 research trace 後修正檢索後段，**通過率 7/11 → 9/11（63.6% → 81.8%）**，0 題退步，幻覺率維持 0。

## 發現的問題

`2330-eps-2025` 的 trace（run `5482653b`）：正確頁有撈到（recall 1.0），但沒進入最終 8 筆 context。

| rerank 後名次 | 頁面 | 內容 |
|---|---|---|
| 1、4、6、8 | 2024 p.53、2023 p.53、2024 p.9、2023 p.9 | **其他年度**年報的股利政策與損益表 |
| 12 | 2025 p.10 | 損益表接續頁，含基本 EPS 66.26 |
| 15 | 2025 p.58 | 每股盈餘附註 |

1. **沒有依問題年度過濾**：問 2025 年，但前 8 名有 4 筆是 2023、2024 年報。
2. **Rerank 語意偏差**：Cohere 把「盈餘分配」排在「每股盈餘」之前（0.96 vs 0.375），這部分不可控。
3. **Trace 失真**：各路先選 2×topK＝16 筆並標為 `Selected`，合併裁到 8 筆時沒有記錄，trace 顯示 p.58 為 `Selected`，實際上沒送進模型。

## 修正（`9e03582`）

- 問題指定年度（西元、民國、區間）且候選中有該年度年報時，rerank 前排除其他年度年報，trace 標記 `DiscardedByFiscalYearMismatch`。
- 合併裁到 topK 後，未進入 context 的候選改標 `DiscardedByTopK`，trace 與實際 context 一致。

只動了 rerank 的輸入與 trace，沒有調整 Cohere 模型、撈回數量或 topK。

## 結果比較

| 指標 | 重新索引後 | 年度過濾後 |
|---|---|---|
| pass_rate | 0.636（7/11） | **0.818（9/11）** |
| facts | 0.50 | **0.83** |
| citation_coverage | 0.60 | **0.85** |
| source_hit | 0.50 | **0.67** |
| retrieval_recall | 0.83 | 0.83 |
| over_refusal_rate | 0.57 | **0.29** |
| hallucination_rate | 0 | 0 |
| latency p50 / p95 | 4.7 s / 18.0 s | 4.7 s / 16.3 s |
| 平均 token | 5,201 | 4,614 |

| 題目 | 重新索引後 | 年度過濾後 | 說明 |
|---|---|---|---|
| `2330-eps-2025` | FAIL | **PASS** | 其他年度年報排除後，2025 年報 EPS 頁進入 context |
| `2454-revenue-2025` | FAIL | **PASS** | 同類問題（撈到但被擠出 context），聯發科未重建索引也修好 |
| `2317-segment-mix` | FAIL | FAIL | p.112 未被檢索到；來源規劃選了 2026Q1 法說會 |
| `2330-no-guaranteed-profit` | FAIL | FAIL | 評分規則語意限制 |
| 其餘 7 題 | PASS | PASS | |

retrieval_recall 不變、pass_rate 上升，代表這次改善來自「撈到之後的選取」，不是多撈到資料。

## 下一步

- **來源規劃**：`2317-segment-mix` 的年度數字題應優先年報，而非最新一期法說會。
- 將抽取規則修正寫回 `scripts/AnnualReportExtractor`，套用到其他公司。

## 檔案

`run/` 為 harness 原始輸出。執行條件同 [2026-11-02 基準](../2026-11-02/README.md)：`ask` 模式、no-judge、一般使用者帳號；API 映像為 `9e03582`。
