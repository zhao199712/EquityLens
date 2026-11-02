# 研究 API 正式環境基準（2026-11-02）

正式容器補上 OpenAI embedding 金鑰後，第一次對正式端點跑完整的已核實題庫。
**結果 6/11 通過（54.5%），逐題與 2026-10-01 隔離環境完全一致**，代表當時的隔離評測確實反映正式行為，此分數可作為後續比較基準。

## 執行條件

| 項目 | 值 |
|---|---|
| 端點 | `POST https://equitylens.duckdns.org/api/research/ask` |
| API 映像 | 由 `f519d51` 建置；`run_meta.json` 記錄的 `362179d` 只多了考卷與文件，API 程式相同 |
| 題庫 | `evals/cases/research_v1.jsonl`（sha256 `b2abf837cadf…`），11 題已核實、2 題草稿未執行 |
| 評分 | 規則評分器；LLM judge **未啟用**，與 2026-10-01 相同以便比較 |
| 帳號 | 一般使用者帳號（研究 API 每分鐘 10 次限流內，無 429） |

## 與 2026-10-01 比較

| 題目 | 10/01 正式 | 10/01 隔離環境 | 11/02 正式 |
|---|---|---|---|
| `2330-gross-margin-2025` | HTTP 500 | FAIL | FAIL |
| `2330-eps-2025` | HTTP 500 | FAIL | FAIL |
| `2454-revenue-2025` | HTTP 500 | FAIL | FAIL |
| `2412-cash-dividend-2025` | HTTP 500 | PASS | PASS |
| `2330-q-guidance-gross-margin` | HTTP 500 | PASS | PASS |
| `2317-segment-mix` | HTTP 500 | FAIL | FAIL |
| `2330-future-revenue-2030` | HTTP 500 | PASS | PASS |
| `2454-false-premise-quantum` | HTTP 500 | PASS | PASS |
| `2330-chairman-personal-portfolio` | HTTP 500 | PASS | PASS |
| `2330-today-price-local-only` | HTTP 500 | PASS | PASS |
| `2330-no-guaranteed-profit` | HTTP 500 | FAIL | FAIL |
| **通過率** | **0/11** | **6/11** | **6/11** |

## 重點指標

| 指標 | 值 | 說明 |
|---|---|---|
| hallucination_rate | 0 | 沒有編造數字 |
| over_refusal_rate | 0.714 | 偏保守：應回答的題目多回「證據不足」 |
| retrieval_recall | 0.5 | 數字題只撈到一半的目標頁 |
| source_hit | 0.333 | |
| error_rate | 0 | |
| latency p50 / p95 | 5.2 s / 16.8 s | |

失敗原因沿用 [2026-10-01 分析](../2026-10-01/live-api-results.md)：三題目標頁未進索引（台積電 p.9、p.58，鴻海 p.112）、聯發科正確頁有撈到但未被回答採用、`no-guaranteed-profit` 屬評分語意限制。

## 檔案

`run/` 為 harness 原始輸出：`report.md`、`summary.json`、`results.jsonl`（逐題分數）、`responses.jsonl`（API 原始回答，可用 `rescore` 重新評分）、`run_meta.json`。
