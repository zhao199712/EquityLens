# VT-GARCH-t + Joint-Vector FHS：ES 正式回測（修訂版）

日期：2026-11-05（修訂：同日，量尺修正後重跑）
資料：私有 seed `seed-v1-20261105`（SHA-256 由 `data/seed/release-lock.json` 鎖定）
程式：`src/EquityLens.Mathematics/src/equitylens_mathematics/research/es_backtest.py`
輸出：`docs/risk/validation/es-backtest/`（summary JSON 與逐日 VaR/ES/PIT）

> **2026-11-05 更正（見 `var-attribution-report.md`）：** 本報告以 Z1、Z2 與「模型自身虛無分配」的 U_ES
> 得出的 ES 低估結論撤回。這些虛無分配把 252 個重抽情境當成真實分配，忽略情境庫是有限樣本；
> 在完全正確的有限情境 FHS 模擬中（T=5,375、97.5%），誤拒率為 U_ES 100%、Z2 88%、Z1 50%。
> VaR 違約率略高於名目值，主要是最近秩分位數的有限樣本偏誤（正確模型下 252 情境的違約率為
> 5.19%／2.70%／1.20%）。違約集中（Christoffersen、C_ES）的結論維持。
> **2026-11-06：** 獨立審查進一步指出「VaR 頻率偏高主要是有限樣本偏誤」與「ES 無低估證據」皆不成立，見 `var-attribution-report.md` 與 `independent-review-report.md`。

## 0. 修訂說明

初版結論在獨立審查後發現量尺問題，已修正並重跑。以下初版說法**撤回**：

| 初版說法 | 問題 | 處置 |
|---|---|---|
| McNeil–Frey 在市值 99% 拒絕（p≈.006） | 離散 FHS + nearest-rank ES 下，正確模型的超額殘差平均不為 0；以 0 為中心的 bootstrap 在 99% 誤拒率約 45% | 改用模型自身分配的 Monte Carlo 虛無分配 |
| C_ES p 值 < 1e-4 | 漸近 χ² 在 T≈1,500 時過大（99%、lag 5 誤拒率約 11%） | 改用 Monte Carlo 虛無分配；漸近值僅列參考 |
| 「檢定大小已驗證」 | 原測試只涵蓋 Z2、20 組、容許 15% | 補六項檢定於離散 FHS fixture 的大小測試 |
| 違約集中「源自 GARCH 反應落後」 | 程式每日更新條件變異數與殘差庫；因果未經逐項驗證 | 撤回；歸因列為後續工作 |
| 研究重播「與 production 逐點一致」 | 只涵蓋 Python，未涵蓋 C# 資料準備 | 見第 2.4 節 |

同時修正兩個影響資料本身的問題（亦影響 production）：

1. **報酬口徑**：C# 回測把實際投組報酬算成加權對數報酬，卻與簡單報酬口徑的 VaR/ES 比較。已改為 Σ wᵢ(Pₜ/Pₜ₋₁ − 1)，Python 輸入驗證拒絕違反此契約的 payload。以初版 VaR 估計，舊口徑在等權 95% 會多出 5 次假違約。本研究重播本身一直使用簡單報酬，未受此影響。
2. **日期對齊**：以共同日期交集對齊會在任一資產停牌時刪掉交易日，把多日變動壓成一筆「日報酬」。seed 內 31 個交易日被刪，樣本外 11 筆報酬實為跨日。現改為：共同涵蓋區間內交易日聯集，缺價日沿用前一有效價格（報酬 0，復牌日反映完整變動）；C# 三個資料準備入口與本研究載入器一致。

## 1. 目的

`five-year-backtest-report.md` 與 `additional-risk-model-validation-report.md`
已完成 VaR 覆蓋率、獨立性與 FZ0 joint loss，但 ESR/Acerbi–Székely 標記為
**Blocked**；FZ0 只能比較模型，不能證明單一模型的 ES 已校準。本報告補上
ES 絕對校準檢定，對象是 production primary 模型本身。

## 2. 方法

### 2.1 預測重播

`collect_forecasts` 逐行重現 `engine.calculate_backtest`：252 日 lookback、
每 21 交易日重估參數、每日更新條件變異數與殘差庫、相同 `deterministic_seed`，
並保留每日 10,000 筆一日預測分配。

### 2.2 檢定與虛無分配

符號沿用 production：報酬、VaR、ES 為左尾負值，breach 為 `actual < VaR`。
**所有 p 值皆以 Monte Carlo 自模型每日預測分配抽出實現值建立虛無分配**
（10,000 次），因為 FHS 一日分配只有 lookback 天數個不同值，各統計量在
正確模型下的中心並非理論值。

| 檢定 | 檢什麼 | 低估方向 |
|---|---|---|
| Acerbi–Székely Z1 | breach 當日損失深度相對 ES | 左尾 |
| Acerbi–Székely Z2 | 頻率 × 深度 | 左尾 |
| McNeil–Frey | breach 日 `(X − ES)/σ` 平均，與模型自身虛無平均比較 | 左尾 |
| Du–Escanciano U_ES | cumulative violation 平均（PIT 尾部頻率與深度） | 右尾 |
| Du–Escanciano C_ES(m) | cumulative violation 自我相關（ES 版違約集中） | 右尾 |

### 2.3 檢定大小與強度

`tests/test_es_backtest.py` 在每日只有 50 個不同情境的離散 fixture 上驗證
六項檢定的大小；另以 100 組、T=600 實測：95% 誤拒率 1–8%，99% 為 0–5%。
McNeil–Frey 99% 由舊法約 45% 降至 2%。強度以尾部放大 1.8 倍與集中違約
fixture 驗證。

### 2.4 跨語言一致性

- Python `RiskBacktestInput` 拒絕 `portfolioReturns` 偏離 Σ wᵢ·expm1(rᵢ) 超過 1e-8 的 payload。
- C# 測試斷言 `PreparePortfolioRiskBacktestInputAsync` 的輸出滿足同一不變式，並驗證停牌日沿用前值。
- 共用 fixture `tests/fixtures/risk_backtest_input.json` 已依此契約重建。
- 完整 DB → C# → Redis → Python 鏈仍由 `tests/EquityLens.Risk.IntegrationTests` 在 CI 驗證，本次未在本機執行。

### 2.5 資料與組合

- 48 檔 0050 成分股 adjusted close（排除 0050 與 7769），2019-09 至 2026-11。
- 樣本外 1,475 日：2020-10-06 至 2026-11-02。
- 等權（HHI 0.021）與 2026-11-04 0050 官方權重正規化的市值權（HHI 0.348）。
- 信心水準 95%、97.5%（Basel FRTB ES 水準）、99%。

## 3. 結果

粗體為 p < .05。

### 3.1 無條件校準

| 組合 | 水準 | Breach / 期望 | Kupiec p | Z1（p） | Z2（p） | McNeil–Frey 觀測／虛無平均（p） | U_ES p | Tail ratio |
|---|---|---|---|---|---|---|---|---|
| 等權 | 95.0% | 77 / 73.8 | 0.700 | -0.054（0.232） | -0.100（0.165） | -0.120／-0.061（0.268） | **0.047** | 1.044 |
| 等權 | 97.5% | 40 / 36.9 | 0.607 | -0.081（0.193） | -0.173（0.105） | -0.217／-0.118（0.235） | **0.006** | 1.066 |
| 等權 | 99.0% | 21 / 14.8 | 0.124 | -0.090（0.472） | -0.551（**0.007**） | -0.293／-0.323（0.531） | **<0.001** | 1.094 |
| 市值 | 95.0% | 79 / 73.8 | 0.535 | -0.060（0.173） | -0.135（0.103） | -0.116／-0.051（0.206） | **0.035** | 1.062 |
| 市值 | 97.5% | 47 / 36.9 | 0.105 | -0.039（0.508） | -0.324（**0.021**） | -0.086／-0.106（0.545） | **0.019** | 1.041 |
| 市值 | 99.0% | 16 / 14.8 | 0.747 | -0.188（**0.033**） | -0.289（0.051） | -0.509／-0.227（0.079） | **0.001** | 1.202 |

Kupiec 取自 `fhs-evt-tail` 執行的同一份 production 預測。

### 3.2 條件校準（違約集中）

| 組合 | 水準 | Christoffersen p | C_ES(1) MC p | C_ES(5) MC p | C_ES(5) 漸近 p（僅參考） |
|---|---|---|---|---|---|
| 等權 | 95.0% | **0.022** | **0.001** | **<0.001** | 5.2e-08 |
| 等權 | 97.5% | **0.024** | **0.004** | **0.002** | 6.1e-08 |
| 等權 | 99.0% | **0.034** | **0.014** | **0.027** | 5.4e-07 |
| 市值 | 95.0% | 0.190 | **0.011** | **0.007** | 7.3e-04 |
| 市值 | 97.5% | 0.077 | **0.007** | **0.023** | 9.5e-04 |
| 市值 | 99.0% | **0.010** | **0.023** | 0.111 | 2.3e-02 |

### 3.3 對齊修正對結果的影響

等權 99% 違約由初版 16 次變為 21 次：實際報酬不變，同日 VaR 變淺（全期 99%
VaR 平均絕對變動 0.13 個百分點）。6 個新增違約日的前 252 日殘差庫皆含舊
對齊合併出的多日報酬；合併報酬幅度大，推高條件變異數與尾部情境，使之後
約一年的 VaR 偏保守。單獨平移重估時點只讓 99% 違約數在 21–23 間變動。
即：**舊對齊錯誤掩蓋了違約，而非製造違約。**

## 4. 判讀

1. **VaR 覆蓋率仍通過。** 六組 Kupiec 皆 p > .10。
2. **尾部頻率與深度合併後偏低估。** U_ES 六組皆拒絕；Z2 在等權 99%、市值
   97.5% 拒絕。主要訊號是尾部 PIT 偏多、偏深，而非單一 breach 的深度。
3. **單次 breach 深度證據薄弱。** Z1 僅市值 99% 拒絕（p=.033）；McNeil–Frey
   校準後無一拒絕。初版「集中組合嚴重低估尾部深度」的強度需下修。
4. **違約集中證據存在，但比初版弱。** C_ES 校準後十二項中十一項 p < .05，
   p 值在 .0002–.03 間，非初版的 < 1e-4。**原因未經驗證**，不歸因於特定機制。
5. **99% 結論對資料細節敏感。** 對齊方式改變即使違約數由 16 變 21；樣本外
   約僅 15 次期望違約與 4–5 段獨立壓力期，99% 各項應視為方向性證據。

## 5. 限制

- 股票池為 2026-11 的 0050 成分股，有倖存者偏誤；市值權重為單日快照；兩組
  組合共用同一時段，不是獨立證據。
- 檢定力有限：1,475 日下，99% 真實違約率 1.5%（低估 50%）只有約四成機率被
  Kupiec 偵測；80% 檢定力約需 20 年資料。
- 未修正參數估計誤差；單一模擬種子集合。
- ESR（Bayer–Dimitriadis）未實作，本報告不宣稱完成 ESR。
- 與 `five-year-backtest-report.md` 的 20 檔研究股票池不同，數字不可直接相比。

## 6. 後續（未實作）

1. **延長歷史**：納入 2008、2015、2018、2020-03 等壓力期，是提升檢定力最有效的方式。
2. **模型歸因**：保存 breach 前後的 α、β、預測波動、標準化衝擊、殘差分位數
   與重估日期，逐項檢驗波動更新、殘差庫更新與尾部支撐。
3. **ES 定義一致性**：一日分配只有 lookback 天數個不同值，ES 取 `≤ VaR`、
   breach 取 `< VaR`，兩者不一致；修正會改變 API 數值，需另行評估。

## 7. 重現

```bash
python3 scripts/seed/fetch.py --output /tmp/equitylens-seed-v1
PYTHONPATH=src/EquityLens.Mathematics/src python3 -m equitylens_mathematics.research.es_backtest \
  --seed-bundle /tmp/equitylens-seed-v1/equitylens-seed-v1.json.gz \
  --constituents data/seed/constituents-2026-11-04.json \
  --output docs/risk/validation/es-backtest
PYTHONPATH=src/EquityLens.Mathematics/src python3 -m pytest src/EquityLens.Mathematics/tests
```

模擬種子由 `deterministic_seed("es-backtest:<組合>", "backtest", offset)` 導出，
檢定虛無分配種子由檢定名稱、組合與水準的 SHA-256 導出。
