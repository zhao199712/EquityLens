# Point-in-time 0050 validation：Blocked design

## 狀態

**Blocked**。Repository 沒有足以建立無未來資訊偏誤的歷史 0050 成分與
權重資料。現有 20 檔 universe 與權重是 2026 terminal snapshot；不得
回填到過去冒充 point-in-time。

## 缺失資料

- 每次真實生效日／再平衡日。
- 生效日當時的完整 0050 constituent identifiers。
- 生效日當時的正式 constituent weights。
- 成分加入、移除與公司行動後 identifier mapping。
- 每日「截至當時已知」的 adjusted-close factor version／knowledge time。

## 所需 schema

```text
index_constituent_snapshot(
  index_code, effective_from, effective_to,
  security_id, ticker, weight, source, source_published_at,
  ingested_at, payload_hash
)

adjustment_factor_version(
  factor_version, security_id, price_date, adjusted_close,
  source_known_at, created_at, superseded_at
)
```

所有回測查詢必須使用 `source_known_at <= forecast_time`，並按
`effective_from <= forecast_date < effective_to` 取成分。

## 建議來源與估時

- 臺灣指數公司／TWSE 官方歷史成分與權重檔；若授權限制不可批次取得，
  需建立逐期受控匯入流程。
- 元大投信歷史 0050 成分揭露可作交叉核對，不替代 index provider 的
  effective-date 來源。
- Schema、匯入、來源 reconciliation、tests 約 3–5 個工程日；資料取得
  與授權時間另計。

正式資料完成後比較：

1. point-in-time 0050；
2. 固定 2026 universe／terminal weights；
3. 固定 universe／equal weights。

