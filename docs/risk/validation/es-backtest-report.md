# VT-GARCH-t + Joint-Vector FHS：ES 正式回測

日期：2026-11-05
資料：私有 seed `seed-v1-20261105`（SHA-256 由 `data/seed/release-lock.json` 鎖定）
程式：`src/EquityLens.Mathematics/src/equitylens_mathematics/research/es_backtest.py`
輸出：`docs/risk/validation/es-backtest/`（summary JSON 與逐日 VaR/ES/PIT）

## 1. 目的

`five-year-backtest-report.md` 與 `additional-risk-model-validation-report.md`
已完成 VaR 覆蓋率、獨立性與 FZ0 joint loss，但 ESR/Acerbi–Székely 標記為
**Blocked**；FZ0 只能比較模型，不能證明單一模型的 ES 已校準。本次補上
ES 絕對校準檢定，對象是 production primary 模型本身。

## 2. 方法

### 2.1 預測重播

`collect_forecasts` 逐行重現 `engine.calculate_backtest`：252 日 lookback、
每 21 交易日重估、非重估日逐日更新 GARCH 變異數與殘差庫、相同
`deterministic_seed`。差別只在保留每日 10,000 筆一日預測分配（ES 檢定需要
完整分配）。`tests/test_es_backtest.py::test_collect_forecasts_matches_production_backtest`
斷言與 production 回測逐點 VaR/ES/actual 相等。

### 2.2 檢定

符號沿用 production：報酬、VaR、ES 皆為左尾負值，breach 為 `actual < VaR`。
所有單尾 p 值的方向均為「風險被低估」。

| 檢定 | 檢什麼 | 虛無分配 |
|---|---|---|
| Acerbi–Székely Z1 | breach 當天的損失深度是否等於 ES（條件於 breach） | 每日從當日 FHS 分配抽實現值的 Monte Carlo，10,000 次 |
| Acerbi–Székely Z2 | breach 頻率 × 深度（無條件） | 同上 |
| McNeil–Frey | breach 日 `(X − ES)/σ` 平均是否為 0 | i.i.d. bootstrap，10,000 次 |
| Du–Escanciano U_ES | cumulative violation `H_t` 平均是否為 α/2 | 漸近常態 |
| Du–Escanciano C_ES(m) | `H_t` 是否自我相關（ES 版違約集中） | χ²(m)，m=1、5 |

Z1/Z2 不採用論文的表列臨界值，而是從每日 FHS 離散分配直接模擬虛無分配。
理由：FHS 分配是有界、離散的，且 production breach 用嚴格不等式；實測 Z1
虛無平均為 −0.025（95%）到 −0.082（99%），並非理論的 0，若用 0 為中心會
系統性誤判。檢定大小已用 20 組校準 fixture 驗證（拒絕率約 5%），強度用尾部
放大 1.8 倍與集中違約 fixture 驗證。

### 2.3 資料與組合

- 48 檔 0050 成分股 adjusted close（排除 0050 本身與歷史過短的 7769），
  共同交易日 2019-09-23 至 2026-11-02。
- 樣本外 1,444 日：2020-10-08 至 2026-11-02（2020-03 落在首個 lookback 內）。
- 等權（HHI 0.021）與 2026-11-04 0050 官方權重正規化的市值權（HHI 0.348，
  2330 約 58%）。
- 信心水準 95%、97.5%（Basel FRTB ES 水準）、99%。

## 3. 結果

### 3.1 無條件校準（ES 大小）

| 組合 | 水準 | Breach / 期望 | Z1（p） | Z2（p） | McNeil–Frey 平均殘差（p） | U_ES z（p 左尾） | Tail ratio |
|---|---|---|---|---|---|---|---|
| 等權 | 95% | 74 / 72.2 | −0.061（.19） | −0.088（.19） | −0.141（.10） | +0.65（.26） | 1.051 |
| 等權 | 97.5% | 40 / 36.1 | −0.072（.25） | −0.188（.09） | −0.190（.11） | +0.88（.19） | 1.052 |
| 等權 | 99% | 16 / 14.4 | −0.147（.13） | −0.271（.06） | −0.464（.051） | +1.28（.10） | 1.133 |
| 市值 | 95% | 77 / 72.2 | −0.063（.16） | −0.134（.10） | −0.125（.08） | +1.18（.12） | 1.067 |
| 市值 | 97.5% | 45 / 36.1 | −0.056（.37） | **−0.316（.023）** | −0.132（.13） | +1.11（.13） | 1.064 |
| 市值 | 99% | 14 / 14.4 | **−0.240（.008）** | −0.202（.09） | **−0.646（.006）** | +1.31（.095） | 1.248 |

### 3.2 條件校準（ES 版違約集中）

| 組合 | 水準 | C_ES(1) p | C_ES(5) p | 排除 PIT=0 日後 C_ES(5) p |
|---|---|---|---|---|
| 等權 | 95% | <1e-4 | <1e-4 | 1.4e-5 |
| 等權 | 97.5% | <1e-4 | <1e-4 | 4.9e-4 |
| 等權 | 99% | <1e-4 | <1e-4 | — |
| 市值 | 95% | .0011 | .0001 | .017 |
| 市值 | 97.5% | .0005 | .0013 | .149 |
| 市值 | 99% | .022 | .206 | — |

95% breach 中，等權 47/74、市值 47/77 筆與另一筆 breach 相距不超過 5 個交易日。

### 3.3 實際損失超出全部模擬的日子

兩組各有 6 天實際報酬低於當日 10,000 筆模擬中的最小值（PIT=0）：

- 等權：2022-03-07、2022-10-11、2024-04-16、2024-08-02、2024-08-05、2025-04-07
- 市值：2022-07-01、2022-10-11、2024-04-19、2024-08-05、2025-04-07、2026-07-17

市值 99% 最嚴重的 breach：2025-04-07 實際 −9.87%，ES −5.59%（1.77 倍）；
2024-04-19 實際 −5.22%，ES −2.97%（1.76 倍）。

## 4. 判讀

1. **平均 ES 大小大致合格，分散組合較佳。** 等權三個水準在 5% 下無一拒絕；
   Basel 97.5% 的 Z2 p=.09。市值組合在 97.5% Z2 與 99% Z1/McNeil–Frey
   拒絕，tail ratio 1.25，代表集中組合在極端日的損失深度被低估約兩成。
2. **低估來自 FHS 的有界支撐。** Joint-vector FHS 只能重抽 252 個歷史殘差
   向量，單日最大模擬損失受限於「窗口內最差殘差 × 當日預測波動」。崩盤首日
   既是新的最差殘差，又發生在 GARCH 尚未上調波動之前，因此 6 天落在模擬
   支撐之外。單一個股權重越高，越無法靠分散稀釋，故市值軌受影響較大。
3. **違約集中是最一致的缺陷。** C_ES 在等權三個水準皆強烈拒絕，且排除 PIT=0
   極端日後仍顯著；不是少數極端值造成，而是 GARCH 對衝擊的反應落後，breach
   成串出現。此結論與 VaR Christoffersen 結果一致，並較其更強。
4. **DCC 不能修正上述兩點。** 先前 DCC 評估顯示相關性只解釋約 1% 的組合
   波動變異，且 DCC 同樣事後反應；C_ES 的問題在波動反應速度與尾部支撐，
   不在相關結構。

## 5. 限制

- 股票池為 2026-11 的 0050 成分股，有倖存者偏誤；市值權重為單日快照。
- 單一模擬種子集合；未修正參數估計誤差（Du–Escanciano 指出主要影響 C_ES 的
  檢定大小）。
- 99% 期望 breach 約 14 次，Z1/McNeil–Frey 在 99% 檢定力有限。
- ESR（Bayer–Dimitriadis）仍未實作；本次以 Acerbi–Székely 與 Du–Escanciano
  取代其角色，不宣稱完成 ESR。
- 與 `five-year-backtest-report.md` 的 20 檔研究股票池不同，數字不可直接相比。

## 6. 後續建議（未實作）

依優先序：

1. **尾部支撐**：在 FHS 殘差庫加入 Student-t 或 GPD（EVT）尾部延伸，讓模擬
   可超出歷史最差殘差；以本報告 Z1/McNeil–Frey 市值 99% 為驗收指標。
2. **反應速度**：VT-GJR（已有研究實作）或以實現波動為輸入的模型，以 C_ES
   與 Christoffersen 為驗收指標。
3. 將 Z2 與 U_ES/C_ES 加入 production `build_model` 輸出前，需先定義 API
   契約與前端呈現；本次僅為研究模組。

## 7. 重現

```bash
python3 scripts/seed/fetch.py --output /tmp/equitylens-seed-v1
PYTHONPATH=src/EquityLens.Mathematics/src python3 -m equitylens_mathematics.research.es_backtest \
  --seed-bundle /tmp/equitylens-seed-v1/equitylens-seed-v1.json.gz \
  --constituents data/seed/constituents-2026-11-04.json \
  --output docs/risk/validation/es-backtest
PYTHONPATH=src/EquityLens.Mathematics/src python3 -m pytest src/EquityLens.Mathematics/tests/test_es_backtest.py
```

結果確定性：模擬種子由 `deterministic_seed("es-backtest:<組合>", "backtest", offset)`
導出，檢定 bootstrap 種子由檢定名稱、組合與水準的 SHA-256 導出。執行時間約
兩到三分鐘（兩組組合，4 核心）。
