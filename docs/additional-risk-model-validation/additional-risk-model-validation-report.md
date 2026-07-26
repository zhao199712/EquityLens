# VT-GARCH 與 MVEWMA 追加穩健性驗證

日期：2026-07-26  
基準 commit：`1281f82`  
研究分支：`python`

## 1. 執行摘要

本次完成 Task 0–8。所有研究均使用既有 adjusted-close panel，不下載、
修改、插值或修補價格；production risk-worker API 與 `main` 均未修改。

核心結果是：VT-GARCH 的優勢確實存在，但強度依投組集中度與時間窗而
變化。等權軌的證據明顯較強，市值集中軌則打平且 rolling 排名反覆。
因此唯一允許的最終結論是 **(c) Workload-dependent**。本次不 promotion、
不實作 routing，也不按 confidence level 拼接模型。

## 2. Task 0–8 完成狀態

| Task | 狀態 | 結果 |
|---|---|---|
| 0 開跑前確認 | Pass | 詳見 `task0-preflight.md` |
| 1 Rolling 子視窗 | Pass | 2 軌 × 2 水準 × 12 windows，完整輸出 |
| 2 排名 bootstrap | Pass | circular block=21、10,000 次、deterministic |
| 3 MCS | Pass | HLN Tmax sequential elimination，含 deterministic test |
| 4 多 confidence | Pass | 90/95/97.5/99/99.5%，同一 daily paths |
| 5 正式 ES | Pass／部分 Blocked | FZ0 joint loss 完成；ESR/Acerbi–Székely 未以不可靠自製近似替代 |
| 6 Refit/lookback | Pass | 9 組全部完成，雙軌 × 95/99 共 36 列 |
| 7 集中度 | Pass | 5 固定 + 200 Dirichlet，410 個比較 |
| 8 決策 | Pass | (c) Workload-dependent |

## 3. Rolling stability

每組有 12 個 252 日視窗、步長 63 日。

| 權重 | 水準 | GARCH 勝率 | MVEWMA 勝率 | ΔQL 中位數 | 判讀 |
|---|---:|---:|---:|---:|---|
| 市值 | 95% | 41.7% | 58.3% | +2.35e-5 | 不支持 GARCH 領先 |
| 市值 | 99% | 58.3% | 41.7% | -1.59e-5 | 混合／不穩定 |
| 等權 | 95% | 83.3% | 16.7% | -5.90e-5 | 時間上穩定領先 |
| 等權 | 99% | 75.0% | 25.0% | -3.96e-5 | 時間上穩定領先 |

`ΔQL = GARCH − MVEWMA`，負值代表 GARCH 較佳。市值軌在兩個水準皆
出現優劣區間翻轉；完整窗口、排名與 breach tests 見
`rolling_window_stability.csv`，逐日累積差見
`cumulative_ql_difference.csv`。

## 4. Bootstrap ranking probability

| 權重/水準 | GARCH 第一名 | GARCH 勝 MVEWMA | ΔQL 90% CI | 強證據三條全過 |
|---|---:|---:|---:|---|
| 市值 95% | 0.6% | 45.6% | [-5.33e-5, 5.81e-5] | 否 |
| 市值 99% | 9.2% | 59.3% | [-4.39e-5, 3.01e-5] | 否 |
| 等權 95% | 3.0% | 96.7% | [-1.38e-4, -7.15e-6] | 否（第一名率不足；GJR 常勝） |
| 等權 99% | 40.4% | 83.4% | [-8.50e-5, 1.88e-5] | 否 |

四模型第一名機率每組皆精確加總為 1。等權 95% 對 MVEWMA 有強烈成對
證據，但 VT-GARCH 並非四模型中穩定第一；因此不能稱為全域冠軍。

## 5. Model Confidence Set

- 市值 95%、市值 99%、等權 99%：90% 與 95% MCS 均保留四模型。
- 等權 95%：90% MCS 淘汰 MVEWMA（p=.0614），保留 VT-GARCH、
  VT-GJR、Ensemble；95% MCS 仍保留四模型（p=.0661）。

沒有任何 90% MCS 只剩 VT-GARCH。結果支持「等權 95% challengers
較好」，不支持 VT-GARCH 單獨建立模型層級。逐輪統計量、p-value 與
淘汰順序見 `model_confidence_set.json`。

## 6. 多 confidence 結果

下表是 VT-GARCH 相對 MVEWMA 的 mean QL 差與 paired block-bootstrap
p-value。

| 權重 | 90% | 95% | 97.5% | 99% | 99.5% |
|---|---:|---:|---:|---:|---:|
| 市值 ΔQL | -1.65e-5 | +2.65e-6 | -1.40e-5 | -5.67e-6 | +1.04e-5 |
| 市值 p | .687 | .938 | .569 | .800 | .643 |
| 等權 ΔQL | -8.35e-5 | -7.08e-5 | -6.38e-6 | -3.10e-5 | -2.33e-5 |
| 等權 p | .051 | .073 | .828 | .332 | .444 |

市值軌方向跨 confidence 改變且皆不顯著；等權五個水準點估計皆改善，
但正式證據集中在 90%/95%。99.5% 的 GARCH 尾部 observation 僅兩軌
各 7 筆，依規格只作描述，不用不顯著 p-value 宣稱等價。

Coherence 全數通過：38,720 個 model-confidence-day joint observations
無 VaR/ES 單調違反、quantile crossing、ES > VaR、NaN 或 Infinity。
違規 CSV 僅含 header，未自動排序或修補。

## 7. ES 正式檢定

已實作 Fissler–Ziegel zero-homogeneous (FZ0) consistent joint VaR–ES
loss，保存完整每日序列。FZ0 是 consistent score，數值不必非負；
驗收檢查的是 ES 嚴格為負及 loss 有限。

VT-GARCH − MVEWMA FZ0 joint loss：

| 權重 | 90% Δ/p | 95% Δ/p | 97.5% Δ/p | 99% Δ/p | 99.5% Δ/p |
|---|---:|---:|---:|---:|---:|
| 市值 | +.0057/.771 | +.0040/.884 | -.0337/.409 | -.0495/.500 | -.0508/.723 |
| 等權 | -.0448/.105 | -.0752/.090 | -.0610/.348 | -.2038/.138 | -.3897/.125 |

等權 95% 的 joint loss 改善在 10% 水準成立，但 95% CI 跨 0；其他主要
比較未達顯著。每列亦附 ES tail-loss ratio 的 90%/95% block-bootstrap
CI 與 tail count。

ESR 與 Acerbi–Székely 標記 **Blocked**：repo 沒有經驗證實作，本次不以
自製近似統計量冒充正式檢定。這不阻塞規格「至少完成」的 FZ consistent
joint loss，但阻止更強的 ES calibration 宣稱。

## 8. Refit/lookback 敏感度

9 組全跑完，convergence failures=0、warnings=0、NaN/Inf=0；near-unit
範圍 0.87%–2.29%，全低於 5%。單次雙軌研究 runtime 約 7.4–49.7 秒。

- lookback 252：等權 95% 在 refit 5/21/63 的 ΔQL 約
  -7.56/-7.09/-7.57e-5，p=.048/.072/.055；市值 95% 均近零。
- lookback 504：等權仍為負，但 p=.176–.189；市值 99% 反而為正。
- lookback 756：95% 兩軌點估計皆偏向 GARCH，但僅等權 refit=5
  達 p=.085；99% 證據弱。

結果不支持為了最低 QL 改參數。baseline 252/21 仍是較簡約且成本適中的
研究預設。完整 alpha/beta/nu/persistence 四分位數、相鄰 refit parameter
jump、VaR path jump、fits、runtime 與警告見 CSV；未發現 finite 或異常
跳動造成的淘汰理由。

## 9. 投組集中度分析

共 205 組 fixtures；權重和最大誤差 6.66e-16，負權重 0。VT-GARCH
點估計優於 MVEWMA：

- 95%：197/205（96.1%）
- 99%：190/205（92.7%）

固定 fixtures 的 ΔQL：

| Fixture | HHI | 95% | 99% |
|---|---:|---:|---:|
| 市值 | .4497 | +2.64e-6 | -5.68e-6 |
| 等權 | .0500 | -7.09e-5 | -3.10e-5 |
| cap 20% | .1000 | +1.74e-5 | +3.00e-6 |
| cap 10% | .0671 | -2.67e-5 | -6.91e-6 |
| inverse-vol | .0617 | -2.98e-5 | -2.07e-5 |

HC1 robust regression `ΔQL = a + b·HHI`：

- 95%：b=+7.91e-5，SE=4.73e-5，95% CI
  [-1.37e-5, +1.72e-4]。
- 99%：b=+5.35e-5，SE=2.44e-5，95% CI
  [+5.60e-6, +1.01e-4]。

正斜率表示越集中，GARCH 的相對優勢越小；99% CI 不跨 0，95% 僅為
方向性證據。`N_eff` 版本兩個 CI 均跨 0。這是描述性分析，不可直接
轉成 production routing。

## 10. 數值健壯性與效能

- 所有 QL、VaR、ES、FZ0 與 regression inputs 有限。
- MCS、ranking、paired tests 與 Dirichlet fixtures 全部固定 seed。
- Bootstrap 第一名機率和為 1。
- 205 組權重均非負且和為 1。
- 9 組 sensitivity fitting failures/warnings 均為 0。
- deterministic artifact payload SHA-256：
  `f971fa07dadda34fd375a7f34b83d7a3dc2bb879ba0a49395b51a9262f7bdc17`。
- 單元測試：4 passed。

## 11. 驗收表

| 項目 | 狀態 | 證據 |
|---|---|---|
| Rolling 子視窗 | Pass | 48 model-window rows，日期與步長完整 |
| Bootstrap determinism | Pass | fixed SHA seeds、unit test、payload hash |
| Ranking probability | Pass | 四組總和均 1 |
| MCS | Pass | 90%/95% HLN Tmax 完整 |
| 多 confidence | Pass | 五水準完整 |
| VaR/ES coherence | Pass | 0 violations |
| ES joint loss | Pass | 38,720 FZ0 daily rows；ESR/AS 另列 Blocked |
| Refit/lookback | Pass | 9/9 組，36 summary rows |
| Near-unit | Pass | 每組列出，最大 2.29% |
| Convergence | Pass | failures=0、warnings=0 |
| 權重 fixtures | Pass | 205 組，和為 1、無負值 |
| Dirichlet fixtures | Pass | 200 組，seed=20260726 |
| Raw/adjusted 資料 | Pass | 零修改 |
| 正式 API/main | Pass | 零修改 |
| Runtime | Pass | 每組及總 wall time 已記錄 |
| PostgreSQL | Pass | 最終 `exited` |
| Git | 完成提交後回填 | branch/head/status 於最終回報 |

## 12. 唯一最終結論

### (c) Workload-dependent

VT-GARCH 在低集中度，尤其等權 90%/95%，有一致且部分達統計顯著的
改善；但市值集中軌 rolling 勝率只有 41.7%/58.3%，MCS 保留 MVEWMA，
ranking 第一機率很低，多 confidence 方向也不一致。集中度回歸在 99%
進一步顯示 HHI 越高，GARCH 優勢越弱。

因此不允許全域輪替，也不允許建立「特定 confidence 使用特定模型」的
路由。MVEWMA-FHS 維持 production primary；VT-GARCH-t + joint-vector
FHS 維持研究預設。

## 13. Promotion 建議

本次 **不建議 promotion**。未來可把 concentration/HHI 列為 routing
研究 feature，但須有獨立新增樣本外資料、事前固定門檻與新的正式驗證，
本次不得實作。

## 14. 修改與產出

研究程式：

- `python/risk_worker/src/risk_worker/research/additional_validation.py`
- `python/risk_worker/src/risk_worker/research/robustness_experiments.py`
- `python/risk_worker/tests/test_additional_validation.py`

所有規格要求的 CSV/JSON、Task 0、execution log、hash 與本報告均位於
`docs/additional-risk-model-validation/`。

## 15. Repository 與 PostgreSQL

- Branch：`python`
- PostgreSQL：`exited`，FinishedAt
  `2026-07-26T13:48:32.856653306Z`
- HEAD SHA 與最終 clean status：提交後於交付訊息回報。

