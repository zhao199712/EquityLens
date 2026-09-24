# Adjusted-Close 資料管線驗收報告

日期：2026-07-26  
分支：`python`  
factor version：`tw0050-total-return-v1-20260726`

## 結論

Phase 5 的 adjusted-close 資料閘門已解除。研究資料表已在遠端
PostgreSQL 驗證並載入，production `market_price` 未被更新；五年
20 檔資料共 24,499 筆，24,498 筆有 adjusted close，唯一無調整值的
raw row 是已明確標記的 2887 零價。另有 2317 缺日記錄在 gap table。

依使用者指示，不使用無法可靠還原或歷史不足的成分股：

- `3037`（資料僅自 2023-07）以現行 0050 成分股 `2344` 替代。
- `6669`（2026 action 與 raw 價格比例方向矛盾）以現行 0050
  成分股 `2412` 替代。
- `2327` 依先前要求不進正式 20 檔 universe；其 2025-08-25
  分割仍保留為 CI fixture。

## 資料與因子決策

- raw close 全程唯讀；調整結果寫入獨立 `risk_research` schema。
- 現金股利採 total-return 口徑。
- yfinance actions 是候選因子；TWSE `TWT49U` 五年官方除權息結果為
  主要核對來源。
- `TWT49U` 明示不包含的減資/分割事件，以 FinMind 的 TWSE 衍生
  reference datasets 補充，原始 payload 留存在 action table。
- 差異超過 0.1% 不自動修補。19 筆逐筆審查後採 TWSE 官方
  pre-close/reference-price 因子，決策記錄於
  `reviewed_official_overrides.csv`。

## 驗收結果

| 項目 | 結果 | 判定 |
|---|---:|---|
| universe | 20 檔 | 通過 |
| raw rows | 24,499 | — |
| adjusted rows | 24,498 | 通過 |
| 已標記 raw invalid | 1（2887 @ 2024-08-22） | 通過 |
| 已標記缺日 | 1（2317 @ 2025-07-30） | 通過 |
| corporate-action events | 139 | — |
| reconciliation accepted | 120 | 通過 |
| reviewed official overrides | 19 | 通過 |
| unresolved/manual review | 0 | 通過 |
| 調整後 \|daily log return\| > 25% | 0 | 通過 |
| CI tests | 9 passed | 通過 |
| 離線 bit reproducibility | 兩次輸出逐檔相同 | 通過 |

確定性來源 snapshot SHA-256：
`c9dca3c8f02c5322788f58f52e7c1d1e28f51ca10e5e297253990674ff1f9f9a`。

最終 CSV SHA-256：

- `price_adjustment.csv`:
  `e5edab241242546ad62b975196bac162436e13f8ab6792079f33d6966bd534df`
- `corporate_action_reconciliation.csv`:
  `b9f2e767217b0e1ef9dbbc071a5107f23d5d8273713c7b6719a1a708f0838ca0`
- `price_gap.csv`:
  `8f70aef1ebbc1b7794135439f3abe77e57e493b4a2a9e229d6e79ced3f64a31b`
- `return_anomalies.csv`:
  `e7d94faca8fc0e8ca2d817b9c4675efd6748c7c034d697ab96d6a9b8b078f592`

## 三年三模型一致性

比較沿用原 Phase 3 的日期、seed、252-day lookback、5,000 simulations、
21-day refit、variance targeting 與權重。為隔離 adjusted-close 效果，
`2344` 與 `2412` 分別接手原 `3037`、`6669` sleeve。

### 市值權重

| 模型 | 信賴度 | breach 舊→新 | QL 變化 |
|---|---:|---:|---:|
| MVEWMA-FHS | 95% | 23→22 | -0.85% |
| MVEWMA-FHS | 99% | 9→8 | +2.05% |
| GARCH-t + Joint-Vector FHS | 95% | 23→25 | -0.04% |
| GARCH-t + Joint-Vector FHS | 99% | 4→4 | -1.31% |
| GJR-GARCH-t + Joint-Vector FHS | 95% | 25→25 | -0.47% |
| GJR-GARCH-t + Joint-Vector FHS | 99% | 6→5 | -1.39% |

市值權重結果穩定，QL 絕對變化皆不超過 2.05%；差異來自 total-return
調整與兩個低權重替代 sleeve。

### 等權重

| 模型 | 信賴度 | breach 舊→新 | QL 變化 |
|---|---:|---:|---:|
| MVEWMA-FHS | 95% | 31→31 | -6.15% |
| MVEWMA-FHS | 99% | 11→11 | -7.58% |
| GARCH-t + Joint-Vector FHS | 95% | 27→28 | -8.41% |
| GARCH-t + Joint-Vector FHS | 99% | 7→7 | -8.46% |
| GJR-GARCH-t + Joint-Vector FHS | 95% | 26→28 | -7.96% |
| GJR-GARCH-t + Joint-Vector FHS | 99% | 8→7 | -9.41% |

等權重差異較大但方向一致改善，原因可定位：兩檔替代股票各占 5%，
其影響遠高於市值權重的低權重 sleeve；breach 只變動 0–2 次，沒有
模型失穩跡象。

## DB 驗證

研究 schema 載入結果：

- `risk_research.price_adjustment`: 24,499 rows
- `risk_research.corporate_action`: 139 rows
- `risk_research.price_gap`: 2 rows
- production universe 的 `market_price.adjusted_close`: 仍為 0 rows

`market_price` 內既有的 250 筆非空 adjusted close 全屬 `AAPL`，建立於
2026-07-13，與本次作業無關。

