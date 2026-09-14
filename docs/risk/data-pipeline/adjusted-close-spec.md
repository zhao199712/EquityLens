# Adjusted-Close 資料管線實作規格（Phase 5 解鎖）

> 日期：2026-07-26
> 性質：task spec（供 Codex 執行）
> 前置：`../validation/risk-engine-validation-plan.md` Phase 4 設計文件已完成
> 範圍：研究分支 + 資料庫新增欄位/表。不修改 raw 價格資料、不動 main、遠端 PostgreSQL 用完停回原狀。

---

## 0. 背景

Phase 5（五年回測）依規格停止，閘門原因：

- production DB：20 檔共 31,678 筆價格，`adjusted_close` 非空 **0 筆**
- 3037 資料僅自 2023-07-17 起
- yfinance 抽查（5 檔）可取得 2021–2026 且能處理 2327 拆分、2887 零價、2317 缺值；但 `repair=True` 在目前 Python/pandas 環境失敗
- FinMind 調整價需付費，不採用

目標：建立**可追溯、通過 corporate-action reconciliation** 的五年 adjusted-close 資料集，解除 Phase 5 閘門。

### 常設規則

1. **禁止任何自動修補**：缺因子、缺事件、資料矛盾 → 標記進人工清單，不插值、不猜測。
2. raw 價格資料唯讀，所有調整結果寫入新欄位/新表。
3. 每個處理決策（含現金股利是否調整）必須在程式碼與報告中留有明確記錄。

---

## 1. 架構決策（已定案，照此實作）

| 決策 | 結論 | 理由 |
|---|---|---|
| 調整因子來源 | **自建因子鏈**：yfinance `actions`（splits + dividends） | `repair=True` 環境失敗；自建因子鏈可逐事件對 TWSE 核對，滿足「可追溯」驗收 |
| 交叉核對 | TWSE OpenAPI 除權息/減資/分割公告 | 雙來源，單一來源錯誤可偵測 |
| 儲存 | **raw / adjusted 雙軌**：raw close 保留，新增 adjusted_close 與 adjustment factor | 風險模型用 adjusted；流動性/成交分析未來需要 raw |
| 拆分與減資 | 一律調整 | 不改變股東價值，不調整會產生假報酬（2327 事件） |
| 現金股利 | 依 Phase 4 設計文件結論執行；若文件未定調，預設**調整**（total return 口徑），並在報告中論證 | 台股配息率高，不調整會在除息日產生系統性假負報酬，污染 EWMA/GARCH 濾波 |

---

## 2. 任務分解

### Task 1：因子鏈建構器
- 對每檔股票從 yfinance 提取 `actions`（splits、dividends，含減資事件）。
- 建立 backward adjustment 因子鏈：最新交易日 factor = 1.0，逐日往前累乘。
  - split/減資事件：`factor = factor × (1 / split_ratio)`
  - 現金股利（若採 total return）：`factor = factor × (close_{ex-1} / (close_{ex-1} + dividend))` 或等價公式，需在程式碼註明。
- `adjusted_close_t = raw_close_t × factor_t`。
- 注意：yfinance 的 split 欄位可能同時包含股票分割與減資，比例方向要逐一驗證（2327 事件為已知測試錨點）。

### Task 2：TWSE OpenAPI 交叉核對
- 抓取 20 檔五年內 TWSE 除權息/減資/分割公告與官方調整參考價。
- 逐事件比對：yfinance 因子 vs TWSE 官方資料，相對差異 > 0.1% 者進人工清單。
- 輸出 reconciliation 報告表：事件日期、股票、兩來源因子、差異、結論（採用哪方、為何）。

### Task 3：已知事件 fixture（CI 測試，三個全過才算完）
1. **2327 @ 2025-08-25**：原始 log return -1.3398（約 -73.8%）。調整後跨事件日 log return 必須落在 ±20% 內，且該事件必須出現在因子鏈中。
2. **2887 @ 2024-08-22 零收盤價**：該日標記為缺值，不產生報酬；不得以前後日硬插值。
3. **2317 @ 2025-07-30 缺值**：同上，缺值日的同步報酬（前後共 4 筆）依現行規則排除。
- 三個 fixture 寫成自動化測試，納入研究分支測試套件。

### Task 4：DB schema 雙軌（研究分支先行）
- 價格表新增 `adjusted_close`、`adj_factor`、`factor_source`、`factor_version` 欄位（或獨立調整表，擇一並說明理由）。
- migration 僅在研究分支與測試 DB 驗證；**不動 main、不動正式 EF migration**。
- `factor_version` 用於未來因子鏈重建時的區分（可追溯要求）。

### Task 5：五年 backfill + 全體異常掃描
- 20 檔 backfill 至 2021-07（五年）。
- 3037：先查資料源是否有早於 2023-07-17 的歷史；有則納入，無則依 Phase 4 文件決策（排除或縮短共同區間），結果寫入報告。
- 全體掃描：所有 |日 log return| > 25% 的點列清單，逐筆歸因（真實事件：財報、崩盤日 / 殘留假報酬）。清單必須收斂到「全部有歸因」。

### Task 6：Phase 5 前置一致性檢查
- 用 adjusted 資料重跑現有三模型三年回測（同 seed、同參數）。
- 與既有結果對比：違約數、QL 差異需可解釋（預期變化不大，因既有三年測試已排除國巨並處理兩筆異常；若有顯著變化，需找出是哪筆調整造成）。
- 此檢查通過 = Phase 5 正式解鎖。

---

## 3. 驗收標準

| 項目 | 門檻 |
|---|---|
| adjusted_close 覆蓋率 | 20 檔 100%（真實停牌/缺值日除外，且逐日標記原因） |
| 三個 fixture | 全過（CI 紅燈即失敗） |
| TWSE reconciliation | 差異事件全部有人工結論，無懸置 |
| 異常報酬掃描 | 清單為空或全部有歸因 |
| 可重現性 | 同指令同 seed 重跑，產出 bit 級一致 |
| 三年回測一致性 | 與既有結果差異可解釋 |

---

## 4. 交付物

1. 因子鏈建構模組 + 單元測試（含三 fixture）
2. TWSE reconciliation 報告
3. backfill 腳本與執行日誌
4. 異常報酬歸因清單
5. 驗收總結（逐條對照第 3 節）
6. 三年一致性對比報告

## 5. 完成後

回報驗收總結。Phase 5（五年回測）的 task spec 將在驗收通過後另行下達，內容依 `../validation/risk-engine-validation-plan.md` Phase 5 節：五年區間、252 lookback、等權重 + 市值權重雙軌、需涵蓋 2022 升息熊市與三個既有壓力期。
