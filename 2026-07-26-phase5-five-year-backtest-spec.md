# Phase 5：五年回測（風險引擎期末考）

> 日期：2026-07-26
> 性質：task spec（供 Codex 執行）
> 前置：`2026-07-26-adjusted-close-pipeline-spec.md` 驗收通過（commit 0bf18d3）
> 依據：`2026-07-26-risk-engine-validation-plan.md` Phase 5 節
> 範圍：研究分支。不動 production `market_price`、不動 main、遠端 PostgreSQL 用完停回原狀。

---

## Task 0：開跑前書面回答（三個遺留問題）

在執行任何回測前，於報告中回答：

1. raw 24,499 筆 vs adjusted 24,498 筆，差的 1 筆是哪一天哪一檔、為何排除。
2. 6669 → 2412 的替換理由（6669 上市日為 2019 年，五年資料理應存在；若為資料品質問題需附證據）。
3. 19 筆 yfinance/TWSE 差異的失敗模式分類：漏事件 vs 比例錯誤，各自占比。此分類供未來 factor_version 升版時界定複查範圍。

---

## 1. 回測設定

| 項目 | 設定 |
|---|---|
| 股票池 | 調整後 20 檔 panel（含 2344、2412 兩檔替換，名單需完整列出並附替換紀錄） |
| 資料 | adjusted close（risk_research schema），2021-07 ~ 2026-07 |
| Lookback | 252 日 |
| 樣本外 | 約 1,000 個預測日（自 2022 年中起） |
| 重估頻率 | 每 21 交易日 |
| 模擬 | 每日 5,000 次 joint-vector residual bootstrap |
| Seed | 沿用既有 SHA-256 derivation（資料矩陣 + 權重 + 預測 offset） |
| 權重軌道 | **雙軌**：市值正規化權重 + 等權重，全部指標分開報告 |

### 參賽模型（四個）

1. **MVEWMA-FHS**：現行 primary（對照組）
2. **VT-GARCH-t + joint FHS**：研究預設（variance targeting 版）
3. **VT-GJR-GARCH-t + joint FHS**：challenger
4. **Ensemble**：VT-GARCH-t 與 VT-GJR 的每日 predictive CDF 50/50 混合（沿用 Phase 2 實作）

### Regime 涵蓋檢查

樣本外必須涵蓋並**分段報告**以下壓力期：
- 2022 升息熊市（台股全年 -22%）
- 2024-08 日圓套利平倉崩盤
- 2025-04 關稅股災
- 2026-03 中東局勢壓力群集

每個 regime 各自輸出違約次數與 QL；平穩期合併為一段。若某 regime 完全落在樣本外範圍之外，需說明。

---

## 2. 評估指標（兩軌 × 兩信心水準）

每個模型 × 權重軌道 × {95%, 99%}：

- 違約次數與違約率、Kupiec p、Christoffersen p
- Quantile loss
- ES tail-loss ratio（公式需於報告附定義）
- **DM 檢定**：四模型兩兩配對對 VT-GARCH-t，以及 VT-GARCH-t vs MVEWMA，HAC/block bootstrap（block=21）
- **子期間穩定性**：樣本外切前後兩半（約 500/500），排名翻轉即標記噪聲
- 近單根擬合比例（variance targeting 後仍須監控，門檻 < 5%）

### 統計注意事項（寫進報告方法節）

- 99% 水準在 ~1,000 個樣本外日下預期違約僅 ~10 次，Kupiec/Christoffersen 檢定力仍然有限；**單一檢定的結論權重不得高於 DM + 子期間 + 雙軌的聯合證據**。
- Christoffersen 在違約數 < 8 時僅作方向性參考。
- 所有模型間差異必附檢定，違者視同未檢定（常設規則）。

---

## 3. 輪替決議規則（升級門檻最終版）

VT-GARCH-t（或 ensemble）升為 primary 的**必要條件全過**：

1. DM p < 0.10（對 MVEWMA 的 QL 差異）：雙軌至少一軌達標，且另一軌無顯著劣化。
2. 子期間排名穩定（兩半子樣本不翻轉）。
3. 近單根比例 < 5%。
4. 無 NaN/Inf、seed 可重現、執行時間在批次可接受範圍（< 10 分鐘單機）。

**允許的結論只有三種**：
(a) 某 challenger 全過門檻 → 建議輪替，附 shadow → primary 切換計畫；
(b) 打平 → MVEWMA-FHS 維持 primary，VT-GARCH-t 維持研究預設，說明下一次重審時機（例如累積更多樣本外日後）；
(c) ensemble 勝出 → 同上 (a) 的切換計畫但對象為 ensemble。

**禁止**：基於單一檢定、單一權重軌道、或單一 regime 的表現做輪替結論。

---

## 4. 交付物

1. Task 0 三問書面回答
2. 完整指標總表（4 模型 × 2 軌 × 2 水準）
3. Regime 分段表
4. DM p-value 矩陣
5. 子期間排名表
6. 輪替決議（限 (a)/(b)/(c) 三種格式）+ 依據
7. 自檢清單（seed、QL 公式、mixture CDF、variance-target 公式、block bootstrap）

## 5. 完成後

回報輪替決議。若結論為 (a) 或 (c)，切換計畫（shadow 期長度、監控告警、回退條件）將另行下達；若為 (b)，本專案進入維護模式，風險引擎議題暫時關閉。
