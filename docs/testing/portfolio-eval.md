# 投組驗收與 eval

研究原題庫的 8 題分母不變。`evals/cases/portfolio_v1.jsonl` 是獨立 14 題：無提示優化、降波動、集中度、指定留現金、指定轉入既有持股、非持股轉入、缺少轉入對象、分散組合、資料不足、保證報酬、偏好衝突、非法加碼、兩組情境及取捨。標的使用中性名稱 A–J，不在名稱洩漏高波動／建議權重。

## 不耗 API 的 CI

既有 eval job 執行 `pytest evals/tests`，包含新 scorer、固定資料、費用攔截器及反例。C# 測試使用真正的情境計算與工具 Guard，另外以注入式 LLM 驗證拒絕／執行錯誤後的修正、checkpoint 恢復及數字修正上限。**注入式模型測試不代表真實 LLM 能恢復。**

```bash
python -m equitylens_eval.portfolio validate
pytest evals/tests -q
```

## 真實模型驗證

僅使用專用隔離 PostgreSQL（名稱以 `_eval` 結尾）與隔離 Redis，先套用既有 migration。不得指定正式服務或將正式帳號加入題庫。

1. 啟動 `equitylens_eval.portfolio_benchmark.serve(5547)`，設定測試程序 `FinMind__BaseUrl=http://127.0.0.1:5547`，基準指數固定 100，避免績效歸因混入外部行情。再啟動本機 API，DeepSeek BaseUrl 必須指向 `equitylens_eval.budget_proxy.serve`，以 `Budget(已授權美元額, 私有ledger路徑)` 控管總額。只允許已定價的 Flash 模型與有界 `max_tokens`；費用不明的請求按整筆保留額計算。不同輪的保留額要扣除前輪費用。停掉這個 API 後才結束代理。
2. Seed 工具限本機 API，且 Docker compose project 必須是 `equitylens-runtime` 或 `equitylens-portfolio-eval`。註冊後會比對該帳號確實存在於指定 DB，才寫入合成行情。獨立測試程序可設 `RateLimiting__Auth__RegisterPermitLimit=100` 建立 14 個各自只有一個投組的帳號，避免選錯投組；不改正式限流。

```bash
python -m equitylens_eval.portfolio_seed \
  --base-url http://127.0.0.1:5545 --container equitylens-runtime-postgres-1 \
  --database equitylens_portfolio_eval --username equitylens \
  --manifest /tmp/portfolio-private.json --as-of 2026-11-03
PORTFOLIO_EVAL_BUDGET_ENFORCED=1 python -m equitylens_eval.portfolio run \
  --base-url http://127.0.0.1:5545 --manifest /tmp/portfolio-private.json \
  --usage-ledger exports/qa/portfolio-eval/provider_usage.jsonl \
  --out exports/qa/portfolio-eval
```

環境旗標是操作者確認代理已啟動，不能取代代理的總額控制。manifest 含短期 token，僅存私有 `/tmp`（0600），不可 commit。種子只新增資料，不刪除資料；API 資料庫不一致時停止。fixture 日期、hash、程式版本、原始問答、工具軌跡、審批前草稿、最後結果及持倉／交易前後快照保存在輸出目錄。

Runner 讀取費用代理 ledger；遇供應商或額度拒絕時標記該題失敗並停止後續題目，不把錯誤後的通用澄清文字當成正確答案。中斷後仍未結算的 reservation 必須全額扣除，才能開始下一輪。

Runner 自動核准的是上述隔離合成帳號的 **WorkflowGate 唯讀報告**；不處理 ToolCall 審批、不修改持倉。此功能不適用正式站。

## 評分與比較

- 任務：情境成功、必要澄清／資料不足／拒絕保證、使用者指定配置與轉入對象、降低波動的方向。
- 數學：Python 獨立重算樣本年化波動、nearest-rank VaR、ES、最大回撤與前後差異，驗證同一快照、權重和 NAV 守恆。
- 副作用：API 持倉與交易前後比較。
- 品質：保存品質關卡結果與問題碼；數字敘述的語義正確性仍需要人工抽查，不能只把 gate 判定當作獨立證明。
- 恢復：只有真的發生 Denied／Failed 後才計算恢復指標。沒有拒絕是 `null`（未觀察），不是通過。
- 費用：summary 的費用是 agent LLM turns，完整路由與模型費用以上游代理 ledger 為準。

```bash
python -m equitylens_eval.portfolio rescore --out exports/qa/portfolio-eval
python -m equitylens_eval.portfolio compare --previous exports/qa/before --out exports/qa/after
```

離線 rescore 不呼叫 API。比較須使用相同 scorer，並核對相同 fixture hash、題目及模型設定；未跑題列出，不當成通過。原始回答不可覆寫。這是小型固定合成資料驗收，不能推論所有真實投組、歷史市場或未來收益。
