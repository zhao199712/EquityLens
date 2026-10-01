# 2026-10-01 隔離演練紀錄

程式與 migration 在隔離工作分支完成，未部署正式服務。正式資料唯讀比對仍為 57 檔、81,837 筆價格，與原備份逐列相同；原 checkout 的部署設定雜湊也未變。

## 演練結果

| 執行 | 成功 | 失敗 | 說明 |
| --- | ---: | ---: | --- |
| 首次完整線上來源演練 | 27 | 30 | 來源缺口／事件衝突拒絕提交 |
| 後續線上重抓 | 10 | 47 | 中途 FinMind HTTP 402，配額不足不宣稱成功 |
| 最終程式與 migration 的凍結來源重播 | 27 | 30 | 新 PostgreSQL 資料庫、逐檔至少間隔一秒 |
| 合併前缺日／同步檢核修正後重播 | 27 | 30 | 開頭缺日檢核、批次完整日期比對；57 檔全部處理完畢 |
| 模擬 2330 提交後驗證失敗 | 0 | 1 | 完整恢復原價格、版本、同步資訊，停止整批 |

重播是已保存原始 HTTP 回應的離線驗證，沒有捏造缺漏來源。27 檔審計快照從 PostgreSQL 讀回後，其 UTF-8 SHA-256 全部與提交前一致。全部更新失敗股票的價格與同步指紋維持原狀。復原演練保留被撤回的調整批次紀錄，並確認全部原始價格與同步欄位逐列恢復。來源配額與尚未確認的事件仍需解決，不能宣稱 57 檔已全數修好。

合併前審查的三項缺口另已修復並新增 16 項回歸案例；完整後端 892 項（含 26 項隔離 PostgreSQL 整合測試）及 Python 22 項通過。最新重播與先前最終演練的 87,344 筆價格逐列完全一致，成功的 27 檔集合相同，原模型輸入與評估結論仍適用。來源快照及逐列比較證據保存於 `exports/price-repair/merge-gaps-fixed-a96c3d5a5c/`，原始歷史報告與雜湊保留。

## 三個指定異常

- **2330**：Yahoo 原始回應中 2021-04-06 完全沒有 timestamp；2025-08-01 有 timestamp 但 OHLC 與 adjusted close 均為 null。這次實際問題屬上游缺漏，不能推論這兩日只是 parser 丟值。程式另外測試 OHLC 缺值但 adjusted close 有效時必須保留。2330 以官方整段因子鏈完成 1,681 筆，新增 45 筆、更新 1,636 筆，全部同一批次且正值。
- **2317 2025-07-30**：TWSE 月行情為成交量 0、OHLC `--`，保留無成交缺口；整段更新成功，不製造價格。
- **2887 2024-08-22**：同樣查到成交量 0、OHLC `--` 的官方證據，但整檔企業事件存在 Yahoo／官方衝突，因此拒絕更新。既有異常資料保持原狀，沒有宣稱已修好，也未納入新回測。

## 測試與公平回測

相關後端 237 項與 Python 22 項通過。包含真實 PostgreSQL 的完整换版、取消／寫入失敗回滾、併發只准一個版本提交、精確來源雜湊，以及 C#／Python 固定因子鏈對照。EF model 與 migration 一致。既有 NU1510／xUnit analyzer 警告不影響通過；RiskObservabilityTests 的全域 listener 需要與其他風險測試分開執行，其 2 項獨立測試通過。

使用最終快照重新建立 **27 檔、28 個連續區段** 的模型輸入。所有完整價格序列與輸入檔 SHA-256 均與已完成的 C# HS／MVEWMA-FHS、正式映像 Python VT-GARCH-t + Joint-Vector FHS 預測相同，因此沿用完全相同輸入的預測並重新計算統計。229,308 筆預測列、432 項比較、0 項模型／評分失敗；原始輸入與重用證據保存於 `exports/price-repair/audited-comparison/`。

全期多重檢定後沒有任何模型相對 HS 的顯著勝出；也沒有 HS 顯著勝出的比較。平均損失多數偏向 FHS／GARCH，但不能據此宣稱普遍具有較強預測力。2408 最後 252 個可用日的 99% 指標有個別 HS 優勢，不能推廣至全體股票。固定 20 檔組合有未驗證成分，本次沒有產生組合排名。模型與正式 API 報酬單位處理均未切換或修正。詳見 [比較報告](comparison/report.html)。

## 未完成更新的股票

下表同時保留首次線上觀察與最後重播的結果。`HTTP ServiceUnavailable` 表示該請求沒有完整凍結來源證據，不能當成交易所資料已補齊。首次舊版「因子衝突」訊息以區間首日顯示，不能作為真實事件日期；最後程式會逐事件給出日期。

| 股票 | 首次線上來源結果 | 最後重播結果 |
| --- | --- | --- |
| 0050 | Official source cannot verify missing/zero price on 06/11/2025. | Official source cannot verify missing/zero price on 06/11/2025. |
| 00835B | Official monthly prices are unavailable. | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 1216 | Unmatched/ambiguous action on 08/04/2023. | Unmatched/ambiguous action on 2023-08-04. |
| 2002 | Unmatched/ambiguous action on 07/26/2024. | Unmatched/ambiguous action on 2024-07-26. |
| 2207 | Unmatched/ambiguous action on 08/04/2023. | Unmatched/ambiguous action on 2023-08-04. |
| 2327 | Official source cannot verify missing/zero price on 10/20/2022. | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2344 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2368 | Official source cannot verify missing/zero price on 09/07/2022. | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2395 | Yahoo／官方因子不一致（舊訊息非事件日期） | Corporate-action factor conflict on 2020-07-31. |
| 2449 | Yahoo／官方因子不一致（舊訊息非事件日期） | Corporate-action factor conflict on 2020-07-13. |
| 2603 | Official source cannot verify missing/zero price on 09/07/2022. | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2880 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2881 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2882 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2883 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2884 | Unmatched/ambiguous action on 07/26/2024. | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2885 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2886 | Yahoo／官方因子不一致（舊訊息非事件日期） | Corporate-action factor conflict on 2020-08-13. |
| 2887 | Yahoo／官方因子不一致（舊訊息非事件日期） | Corporate-action factor conflict on 2020-08-11. |
| 2890 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2891 | Unmatched/ambiguous action on 07/13/2026. | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 2892 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 3017 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 3037 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 3653 | Yahoo／官方因子不一致（舊訊息非事件日期） | Corporate-action factor conflict on 2020-08-20. |
| 3665 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 5880 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 6669 | Yahoo／官方因子不一致（舊訊息非事件日期） | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 6919 | Official monthly prices are unavailable. | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |
| 7769 | Official monthly prices are unavailable. | Source failure (HttpRequestException, HTTP ServiceUnavailable); no prices were committed. |

精確雜湊與逐檔結果保存於 [rehearsal-summary.json](rehearsal-summary.json)。原始備份、來源快照、測試 log 與復原證據保留在忽略 Git 的 exports；隔離 PostgreSQL 容器在匯出後清理。正式部署及全資料庫復原流程見 [README](../README.md)。
