# 台股匯入與還原權息完整性修復

本次修復在 `research/hs-fhs-garch` 隔離工作分支完成，沒有部署或修改正式資料庫。2026-10-01 的備份演練涵蓋 57 檔台股、2019-11-01 至 2026-09-30：27 檔完成完整歷史換版，30 檔因來源或品質不足未提交。詳見 [演練紀錄](2026-10-01/rehearsal.md) 與 [三模型比較](2026-10-01/comparison/report.html)。原始價格、來源 HTTP 回應與逐日預測僅留在忽略 Git 的 `exports/price-repair/`。

## 更新流程

所有 TWSE／TPEX 的 import、依 ticker import、sync、refresh 共用 `TaiwanPriceImportCoordinator`。

1. 獨立讀取證券及既有整段日線，計算包含 raw、adjusted close、版本及同步欄位的 SHA-256 指紋。
2. 在交易外重新擷取完整歷史 raw prices 及 Yahoo adjusted close。後者獨立解析，不要求同日 OHLC 完整；檢查日期、時區、陣列長度、重複及正值。回應股票身分錯誤直接拒絕。
3. 取得 FinMind 官方衍生交易日曆，延伸至要求截止日後 14 天以檢查來源是否過期。異常零價格、缺漏日向 TWSE／TPEX 月行情查證：有效 OHLC 可更正；明確零成交與無價格才保留無成交缺口；單純查無一列不能當休市。
4. Yahoo 全段有效時使用同次回應。缺值時以整段官方事件重建，TWSE／TPEX 直接事件優先，官方不可取得的歷史區間保存 FinMind 官方衍生事件。相互衝突、同日多筆未能確認合併、事件清單不一致均拒絕。
5. 現金 `(P−D)/P`、拆股 `1/R`；配股、增資、減資使用官方合併事件因子。只調整事件日前的價格，不使用舊 adjusted close 補洞、不插補、不用 raw close 假裝還原價。
6. 提交使用新的 DbContext、Serializable 交易與證券 `FOR UPDATE`，重新核對擷取前指紋。raw、整段 adjusted close、審計批次、價格版本與同步資訊一起提交。失敗／取消回滾並清除追蹤器；版本變動必須重新擷取再試。

台股自動 sync／refresh 的截止日採台北時間，18:00 前以昨日為完整日線上限；保守截止規則避免盤中價格被視為完整日線。「今天已同步」還要求有效批次、要求區間涵蓋、完整交易日截止、每列正值及批次一致；不能只靠同步時間。

## 合併前審查修正

針對區間開頭缺日、已同步略過缺列與 ticker 回應漏欄位，新增 16 項回歸案例。舊實作先重現失敗，修正後相關 21 項案例全部通過；完整後端 892 項及 Python 22 項通過，真實 PostgreSQL 的 26 項整合測試包含在後端計數中。既有監控 listener 的 2 項測試分開執行以避免平行競態；EF model 與 migration 一致，這次不新增 migration。

- 來源查證與候選檢核都從要求的完整歷史起日開始。開頭缺日只能用官方實際行情補回，或以明確無成交證據保留缺口；不能從來源第一筆推論上市日。沒有可驗證的掛牌前／無成交證據就拒絕更新，價格、批次及同步資訊維持原狀。
- 「今天已同步」核對整個批次的交易日，扣除審計中已確認無成交的日期，再與資料庫實際日期逐日比對。首日、中間日、末日或近期要求區間之外的歷史列被刪除，都必須重新擷取；無成交日若出現價格也不得略過。
- 重新擷取範圍保留上一驗證批次的起訖日，避免最早或最晚價格列被刪除後縮短歷史範圍。
- 依 ticker 匯入回應傳遞已提交批次的調整來源、版本、起訖日及驗證截止日，與依證券 ID 匯入一致。

修正後以原 57 檔、2019-11-01 至 2026-09-30 範圍再次重播凍結來源，仍為 27 檔成功、30 檔來源／品質不足而未提交，整批未觸發停止。新舊快照的 87,344 筆價格在日期、OHLC、adjusted close、成交量與 raw 來源上逐列完全一致，成功股票集合亦相同；因此這次修正沒有改變先前模型評估的價格輸入。新重播與逐列比對證據保存在忽略 Git 的 `exports/price-repair/merge-gaps-fixed-a96c3d5a5c/`。

相關回歸見 `TaiwanPriceEvidenceProviderTests`、`MarketPriceServiceAdjustedCloseBackfillTests` 與 `TaiwanPriceImportPostgresTests`。原始測試 log 留在本機 `/tmp/equitylens-stock-merge-review-20261001/`，不納入 Git。以上驗證不修改正式服務或資料庫，也不切換風險模型。

## Schema 與介面

Migration：`20261001074352_AddVerifiedPriceAdjustmentBatches`。新增 `price_adjustment_batch`，記錄來源、擷取時間、涵蓋日期、已驗證截止日、算法版本、來源快照 SHA-256 與完整序列 SHA-256。`snapshot_json` 使用 **text** 保留原始 JSON 字元，避免 PostgreSQL jsonb 重排內容後無法重算雜湊。每日價格與證券目前批次都有外鍵；原有資料的批次及驗證截止為 null，明確保持未驗證。

既有路由及請求格式保留。成功回應新增 `adjustmentSource`、`adjustmentVersion`、`coverageFrom`、`coverageTo`、`verifiedThrough`。既有 bulk refresh 仍保留失敗清單及成功／失敗／略過計數。

| 情況 | HTTP | 錯誤碼 |
| --- | --- | --- |
| 缺值、非正價格、日期重複、來源事件衝突、截止不足 | 422 | `market_price.quality_error` |
| HTTP／來源服務／來源回應失敗 | 502 | `market_price.provider_error` |
| 擷取途中版本改變或交易序列化衝突 | 409 | `market_price.update_conflict` |

## 可重現的隔離演練

`research/EquityLens.PriceRepair` 只建立新的隨機命名資料庫，不寫入提供連線中的既有資料庫。請把 `EQUITYLENS_PRICE_REPAIR_POSTGRES` 指向隔離 PostgreSQL 管理連線，伺服器需支援專案 migration 使用的 vector 擴充；本次使用 ParadeDB PostgreSQL 18。

```bash
# 連線、FINMIND_TOKEN 由環境提供，不保存憑證。
dotnet run --project research/EquityLens.PriceRepair/EquityLens.PriceRepair.csproj -- \
  exports/model-comparison/before.json exports/price-repair/new-rehearsal

# 已保存 HTTP 原始回應的離線重播：找不到相符證據即失敗，不製造價格。
EQUITYLENS_PRICE_REPAIR_SOURCE_REPLAY=exports/price-repair/frozen-sources \
  dotnet run --project research/EquityLens.PriceRepair/EquityLens.PriceRepair.csproj -- \
  exports/model-comparison/before.json exports/price-repair/new-replay
```

每次要求新的輸出目錄，保留原始備份與 SHA-256。2330 優先，逐檔至少相隔一秒；來源／品質失敗記錄並繼續。每次拒絕提交核對整段資料指紋未變；成功後檢查正值、版本一致及截止日。

研究工具可用 `EQUITYLENS_PRICE_REPAIR_SIMULATE_VERIFICATION_FAILURE=2330` 模擬提交後驗證失敗。工具鎖定股票、核對提交後指紋，恢復原價格、版本及同步資訊並逐欄核對復原結果，保留被撤回的批次審計；然後停止整批。復原失敗或出現其他寫入者亦停止並拋錯。本次確實在新的 PostgreSQL 資料庫跑過此演練；沒有對正式資料執行故障注入。舊 `scripts/refresh_tw_prices.py` 復原器在發現新批次欄位時明確拒絕，避免只復原同步時間卻遺漏版本。

## 測試與回測

後端 tests 覆蓋獨立 Yahoo 解析、兩個指定缺值日、官方月行情、正值／缺漏／事件衝突、現金／拆股／配股／減資／同日合併及今天同步略過。C# 與 Python 使用同一固定 JSON 因子鏈對照。

設定 `EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING` 指向隔離實例後，`TaiwanPriceImportPostgresTests` 驗證完整換版、重複匯入、來源缺值保持狀態、Save 後取消與寫入失敗回滾、兩個相同舊版本候選只准一個提交、來源快照讀回後 SHA-256 一致。交易驗證不依賴 EF InMemory。

```bash
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj \
  --filter 'FullyQualifiedName~MarketPrices|FullyQualifiedName~MarketData|FullyQualifiedName~TaiwanPriceImportPostgresTests|FullyQualifiedName~CleanPostgresDatabase'

PYTHONPATH=src/EquityLens.Mathematics/src python -m pytest \
  src/EquityLens.Mathematics/tests/test_adjusted_close.py \
  src/EquityLens.Mathematics/tests/test_api_adjustment_golden.py \
  src/EquityLens.Mathematics/tests/test_engine.py \
  src/EquityLens.Mathematics/tests/test_hs_comparison.py

PYTHONPATH=src/EquityLens.Mathematics/src python -m equitylens_mathematics.research.hs_comparison prepare \
  --snapshot exports/price-repair/rehearsal-final-audit/snapshot.json \
  --directory exports/price-repair/fresh-comparison --verified-only \
  --calendar exports/price-repair/source-probes/calendar.json --to 2026-09-30
```

後續 C#／正式映像 GARCH 執行與比較入口詳見 `research/EquityLens.RiskEvaluation`、`scripts/run_risk_research_container.py` 及前次模型評估文件。模型輸入檔必須由同一凍結快照產生；僅在每個輸入檔與完整序列雜湊完全相同時可沿用已完成預測。這次重新建立輸入與評分，27 檔、28 段輸入逐檔雜湊皆與完成的預測一致，留下 `reuse-evidence.json`。不將未驗證股票混進公平回測。

重新執行預測與產生報告：

```bash
dotnet run --project research/EquityLens.RiskEvaluation/EquityLens.RiskEvaluation.csproj -- \
  exports/price-repair/fresh-comparison/inputs exports/price-repair/fresh-comparison/csharp

python scripts/run_risk_research_container.py \
  --directory exports/price-repair/fresh-comparison --stage garch \
  --image sha256:8160b1745ab22d320410b80d07ba89e2546ae786272b5dc6e3863e7d529bbb65

PYTHONPATH=src/EquityLens.Mathematics/src python -m equitylens_mathematics.research.hs_comparison analyze \
  --directory exports/price-repair/fresh-comparison

python scripts/report_hs_comparison.py --directory exports/price-repair/fresh-comparison \
  --repair-directory exports/price-repair/new-replay --output docs/stock-price-integrity/new-comparison
```

官方因子規則參考 [TWSE 除權息計算](https://www.twse.com.tw/zh/announcement/ex-right/twt49u.html)；櫃買中心事件來源使用 [TPEX 除權息預告](https://www.tpex.org.tw/zh-tw/announce/market/ex/cal.html)。

## 正式部署與復原順序

正式部署仍需使用者另行確認。本次未部署 migration、未修改正式模型與報酬單位處理。

1. 備份整個正式 PostgreSQL（含安全保存的來源審計），在另一個資料庫實際驗證可還原。
2. 對測試環境套用 migration，確認舊資料保持未驗證；依 2330 優先流程驗證整段更新。
3. 確認來源配額與尚未解決的股票事件，再部署同一版本程式與 migration。每檔缺證據的股票維持失敗，不能宣稱全 57 檔已修復。
4. 更新時每檔至少隔一秒，若提交後品質退化或復原失敗即停止整批。API 提交失敗已原子回滾，不以不完整請求再覆蓋資料。
5. 若正式部署須撤回，用已驗證可還原的全資料庫備份恢復，保留事件期間新資料與審計的副本供調查，避免直接刪 migration 欄位遺失追溯資料。

本次臨時 PostgreSQL 容器在匯出快照、批次、測試與復原證據後清理；重新演練會建立新資料庫。
