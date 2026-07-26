# VT-GARCH 與 MVEWMA 追加穩健性驗證：Task 0

日期：2026-07-26

## 開跑前確認

1. 五年回測基準 commit：`1281f82`。
2. 固定設定：
   - lookback：252 個交易日
   - refit interval：21 個交易日
   - simulations：每日 5,000 paths
   - circular block-bootstrap block length：21 個交易日
   - bootstrap repetitions：10,000
   - confidence levels：95%、99%
   - seed derivation：先對同步 returns matrix 與投組 weights 的
     float64 bytes 計算 SHA-256，再以既有 `stable_seed` 加入 prediction
     offset；模型比較使用共同 offset seed。
3. `phase5-market.json` 與 `phase5-equal.json` 已保存兩軌、四模型、968
   個樣本外日的 prediction／VaR／ES 序列；QL 可由相同公式逐日確定性
   重建，因此 Task 1–3 可直接重用。Task 4 的新增 confidence levels
   及 Task 6–7 的新設定需重算。
4. 本次只讀既有 adjusted-close input
   `/private/tmp/equitylens-adjusted-close/phase5-five-year-input.json`；
   不重新下載、不回寫、不插值、不修補 adjusted-close。
5. 預計輸出位於 `docs/additional-risk-model-validation/`：
   `rolling_window_stability.csv`、`cumulative_ql_difference.csv`、
   `bootstrap_model_ranking.json`、`model_confidence_set.json`、
   `multi_confidence_backtest.csv`、`var_es_coherence_violations.csv`、
   `es_joint_loss_results.csv`、`refit_lookback_sensitivity.csv`、
   `portfolio_weight_manifest.csv`、`concentration_robustness.csv`、
   `additional-risk-model-validation-report.md`、`execution.log`、
   `deterministic_payload.sha256` 與測試輸出。
6. 本次不新增 DCC、Copula、EGARCH、其他 GARCH 變體或新 ensemble；
   不修改 production `risk-worker` API、`main` 或任何價格資料。

