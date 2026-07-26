# VT-GARCH workload routing 正式驗證報告

日期：2026-07-27  
研究分支：`python`  
開跑前 HEAD：`796be3263c298a0a011c1fa8a8d93a25460dad87`

## 1. 執行摘要

本次以事前鎖定且執行後未修改的 routing manifest，完成 8 階 breadth、
643 個不重複 universe、1,929 個 universe-weight workloads、968 個樣本
外日、每日 5,000 paths 的 MVEWMA/VT-GARCH 比較。

樣本內全域 VT-GARCH 的平均 QL、FZ0、coverage 與 regret 均優於全域
MVEWMA；但事前固定的 HHI routing 只選到 11.3% workloads 使用 GARCH，
效果遠弱於全域 GARCH。HHI、maximum weight、N_eff 具有嚴重
multicollinearity，條件係數會翻向；first-PC 對 QL 有訊號但無法跨 FZ0
穩定。門檻附近無 hysteresis 會出現 1 日 dwell 與最多 32 次切換。

此外，全部資料都曾參與 workload hypothesis 形成；repo 無真正
point-in-time 0050 或新增獨立 holdout。因此唯一結論是：

> **(d) 不支持 routing。**

MVEWMA 維持 production primary；VT-GARCH 維持 research/shadow
challenger。本次不允許正式 routing、不 promotion。可進行「雙模型純
觀測 shadow logging」，但不能讓 routing 影響使用者輸出。

## 2. Task 0–9 狀態

| Task | 狀態 | 結果 |
|---|---|---|
| 0 Preflight/manifest | Pass | manifest 事前建立，SHA 驗證不變 |
| 1 Breadth ladder | Pass | 20/15/10/7/5/3/2/1 全部完成 |
| 2 Driver analysis | Pass | 單變量、多變量、HC1、VIF、bootstrap stability |
| 3 固定策略 | Pass | A–E 未改門檻；E 僅診斷，不具跨 loss eligibility |
| 4 公平比較 | Pass | per-workload、equal synthetic、breadth/HHI strata、worst 5% |
| 5 Threshold stability | Pass | 8 邊界 paths × 4 governance × 968 日 |
| 6 Failure/fallback | Pass | 14 fixtures，全有 reason code，silent repair=0 |
| 7 Point-in-time | Blocked | 缺歷史成分、權重、再平衡與 knowledge-time factor |
| 8 Independent OOS/shadow | Blocked/Pass design | 無 holdout；forward/shadow 規格完成 |
| 9 決策 | Pass | (d) 不支持 routing |

## 3. Breadth ladder 實測

下表為三種權重及所有固定 universe sampling 的平均
`ΔQL = GARCH − MVEWMA`；負值代表 GARCH 較佳。「勝率」是 workload
點估計 GARCH 較佳的比例，不等同統計顯著率。

| Breadth | Workloads/水準 | 95% ΔQL / 勝率 | 99% ΔQL / 勝率 |
|---:|---:|---:|---:|
| 20 | 3 | -3.80e-5 / 100% | -2.24e-5 / 100% |
| 15 | 312 | -2.37e-5 / 82.1% | -1.62e-5 / 85.3% |
| 10 | 312 | -2.16e-5 / 78.8% | -1.24e-5 / 80.4% |
| 7 | 312 | -2.24e-5 / 79.2% | -7.16e-6 / 68.3% |
| 5 | 312 | -1.46e-5 / 70.8% | -4.24e-6 / 64.7% |
| 3 | 312 | -1.15e-5 / 67.3% | -5.61e-6 / 64.7% |
| 2 | 306 | -1.82e-5 / 69.0% | -9.23e-6 / 66.0% |
| 1 | 60 | -1.11e-5 / 65.0% | -9.33e-6 / 65.0% |

所有 breadth 的平均 ΔQL 與 ΔFZ0 都偏向 GARCH，但優勢在較窄 breadth
通常變弱，且單一 workload 仍可能反向。這推翻「只有 20 檔以上才值得
GARCH」的簡化說法，也不支持用 nominal count 作硬 routing。

## 4. 10/5/3/2/1 檔直接比較

- 10 檔：95%/99% 勝率 78.8%/80.4%，平均 ΔQL
  -2.16e-5/-1.24e-5。
- 5 檔：70.8%/64.7%，平均 -1.46e-5/-4.24e-6。
- 3 檔：67.3%/64.7%，平均 -1.15e-5/-5.61e-6。
- 2 檔：69.0%/66.0%，平均 -1.82e-5/-9.23e-6。
- 1 檔：65.0%/65.0%，平均 -1.11e-5/-9.33e-6。

1/2 檔並未顯示 GARCH 必然失敗；但優勢比例與效果量不足以形成
「持股數門檻」。所有個別 workload 的 QL/FZ0、paired block-bootstrap
CI/p、coverage、ES ratio 與 runtime 均在
`breadth_validation_results.csv`。

## 5. Workload driver 分析

### 單變量

95% QL 的標準化係數（90% CI）：

- HHI：+8.85e-6，[+7.66e-6, +1.00e-5]
- N_eff：-9.60e-6，[-1.05e-5, -8.69e-6]
- maximum weight：+1.06e-5，[+9.52e-6, +1.17e-5]
- nominal count：-3.53e-6，[-4.64e-6, -2.43e-6]
- first-PC：+4.18e-6，[+2.71e-6, +5.64e-6]

方向上仍是越分散、GARCH 越有利，但不只 HHI 有訊號。

### 多變量與共線性

- HHI VIF=36.0、maximum weight VIF=36.8、N_eff VIF=9.8；
  不得把條件係數作因果或獨立門檻證據。
- 在同時放入高度相關特徵後，HHI 係數翻為負、maximum weight 為正，
  顯示係數分解不穩定。
- first-PC 對 QL 的 bootstrap selection stability 為 95% 的 83.1%、
  99% 的 99.6%，但對 FZ0 僅 16.5%/10.8%，不能稱為跨 scoring target
  穩定。
- average correlation 對 95% QL/FZ0 穩定，但對 99% QL selection
  stability 僅 13.3%。
- nominal count 控制其他特徵後的 99% QL stability 僅 23.5%，不能用
  nominal count routing。

修正後 workload hypothesis 是：「分散度、相關結構與 volatility 共同
關聯模型差異」，不是 HHI 單因子。現有特徵效果不足以形成可靠 routing
feature。

## 6. 全域與 routing 策略比較

所有 workload 等權 aggregate；因無 production workload distribution，
frequency weighting 明確標記 synthetic equal。

### 95%

| Strategy | Mean QL | Mean FZ0 | Coverage error | Worst regret | GARCH share |
|---|---:|---:|---:|---:|---:|
| A 全 MVEWMA | .00182415 | -3.41318 | .00784 | .00014778 | 0% |
| B 全 GARCH | .00180568 | -3.43076 | .00313 | .00011720 | 100% |
| C HHI | .00182025 | -3.41761 | .00682 | .00014778 | 11.3% |
| D HHI+N_eff | .00182025 | -3.41761 | .00682 | .00014778 | 11.3% |
| E HHI+PC 診斷 | .00182025 | -3.41761 | .00682 | .00014778 | 11.3% |

### 99%

| Strategy | Mean QL | Mean FZ0 | Coverage error | Worst regret | GARCH share |
|---|---:|---:|---:|---:|---:|
| A 全 MVEWMA | .00058207 | -2.80930 | .01153 | .00008690 | 0% |
| B 全 GARCH | .00057291 | -2.90824 | .00320 | .00007699 | 100% |
| C HHI | .00057947 | -2.82898 | .01001 | .00008690 | 11.3% |
| D HHI+N_eff | .00057947 | -2.82898 | .01001 | .00008690 | 11.3% |
| E HHI+PC 診斷 | .00057947 | -2.82898 | .01001 | .00008690 | 11.3% |

全域 GARCH 對 MVEWMA 的平均 QL 效果：

- 95%：-1.85e-5，約改善 1.01%；universe-cluster bootstrap
  p<.0001，95% CI [-2.05e-5, -1.64e-5]。
- 99%：-9.16e-6，約改善 1.57%；p<.0001，
  95% CI [-1.05e-5, -7.86e-6]。

HHI routing 雖相對 MVEWMA 有小幅顯著改善，但 QL 只改善約
0.21%/0.45%，且明顯落後全域 GARCH。D 在數學上與 C 重合：
`HHI < .10` 已必然推出 `N_eff > 10`。本資料中 E 也選到相同集合，
且 first-PC 不具跨 FZ0 eligibility。

因此 routing 沒有比全域策略提供額外模型選擇價值。

## 7. Regret

QL regret：

| 水準 | Strategy | Mean | P95 | Max |
|---|---|---:|---:|---:|
| 95% | 全 MVEWMA | 2.37e-5 | 6.63e-5 | 1.48e-4 |
| 95% | 全 GARCH | 5.23e-6 | 3.57e-5 | 1.17e-4 |
| 95% | HHI routing | 1.98e-5 | 6.58e-5 | 1.48e-4 |
| 99% | 全 MVEWMA | 1.38e-5 | 4.04e-5 | 8.69e-5 |
| 99% | 全 GARCH | 4.60e-6 | 2.94e-5 | 7.70e-5 |
| 99% | HHI routing | 1.12e-5 | 3.84e-5 | 8.69e-5 |

固定 routing 的 tail/max regret 幾乎保留全 MVEWMA 的缺點，沒有取得
全域 GARCH 的較低 regret。這是 routing 尚未成熟的直接反證。

## 8. Threshold stability

無 hysteresis：

- 8 條邊界 path 的 switch count 為 8–32 次。
- 最短 dwell 可為 1 日，中位 dwell 最短為 2 日。
- 最大同日模型 VaR gap 約 2.78%，ES gap 約 2.50%。
- 最大 selected VaR temporal jump 約 4.20%，ES jump 約 4.02%。

治理候選確實降低 switching：

- 0.08/0.12 hysteresis：0–2 次切換。
- minimum dwell 21：4–12 次。
- 每 21 日評估：2–4 次。

但 governance 只是抑制症狀，不能修復 routing feature 不穩定或提升策略
效果。直接 HHI threshold 不適合 production；本次不正式實作任何
hysteresis。

## 9. Failure 與 fallback

14 個 fixtures 全部產生完整 audit fields：

- current healthy VT-GARCH：3。
- previous stable parameters：5。
- fail closed：6。
- silent clipping/interpolation：0。
- confidence/window 自動變更：0。

1/2 檔合法資料可直接使用 VT-GARCH。persistence、optimizer、
non-finite、timeout 等會使用 previous stable parameters；歷史不足、
大量缺值、非同步日、零變異、DB unavailable、incomplete adjusted
coverage 在 MVEWMA 也無合法輸入時 fail closed。這是研究決策表，尚未
接入正式 API。

## 10. Point-in-time

**Blocked**。Repo 無歷史 0050 effective-date constituents、weights、
rebalance dates 與 knowledge-time adjustment factors。固定 2026
universe/terminal weights 與固定 equal universe 已完成，但兩者都是
in-sample，不能替代 point-in-time。缺失 schema、來源與 3–5 工程日估時
見 `point_in_time_blocked_design.md`。

## 11. 獨立樣本外狀態

**Blocked**。Cutoff 2026-07-16 後沒有同 factor-version 的新增共同有效
交易日；也沒有未參與 hypothesis 的另一 universe。已建立 append-only
forward spec：

- 250 新日：初檢，不調門檻。
- 500 新日：正式 promotion review。
- 或至少兩個事前定義壓力事件。

新規則必須建立新 manifest；不得覆寫本版取得「獨立」結果。

## 12. Shadow production 設計

已提供完整 logging schema，真實 workload 同時記錄兩模型 VaR/ES、
routing recommendation、realized QL/FZ0、breach、runtime、fit health、
fallback、HHI/N_eff/correlation、model/factor version。

允許的是**純觀測 shadow**：production response 仍只顯示 MVEWMA。
不允許讓本次 HHI routing 改變使用者輸出。

## 13. 驗收表

| 項目 | 狀態 | 證據 |
|---|---|---|
| Breadth ladder | Pass | 8 breadth 全部完成 |
| Universe sampling | Pass | 主要 breadth 100+；20/1 使用全部合法 |
| Weight manifest | Pass | 1,929 組，非負，sum error≤4.44e-16 |
| Workload features | Pass | breadth、HHI、N_eff、weight、correlation、PC、sector、vol、missing |
| Routing manifest | Pass | 執行前 SHA，結束後一致 |
| Strategy comparison | Pass | A–E、equal synthetic、strata、worst 5% |
| Statistical evidence | Pass | effect、cluster bootstrap p/CI |
| Regret | Pass | mean/median/P90/P95/max |
| Threshold stability | Pass | switch/dwell/VaR/ES jump |
| Failure fixtures | Pass | 14/14 reason/audit 完整 |
| Silent repair | Pass | 0 |
| Point-in-time | Blocked | 未偽造；blocked design 完整 |
| Independent holdout | Blocked | forward spec 完整 |
| Raw/adjusted data | Pass | 零修改 |
| API/main | Pass | 零修改 |
| Determinism | Pass | manifest/seed/sharding/tests |
| Runtime | Pass | 正式 run 3001.32 秒 |
| PostgreSQL | Pass | 本次未使用；最終狀態 `exited` |
| Git | 提交後回報 | `python` 分支 |

## 14. 唯一最終結論

### (d) 不支持 routing

Breadth 實驗顯示 GARCH 優勢不是由簡單 HHI 或 nominal count 門檻穩定
分割；事前 HHI routing 顯著落後全域 GARCH，driver 受共線性與 scoring
target 影響，threshold 無治理時又會頻繁切換。原 HHI 關係應標記為
原樣本內發現，不建立正式 routing。

## 15. 是否允許進入 shadow

- **允許雙模型純觀測 shadow logging**，不影響 production output。
- **不允許 HHI routing 進入 decision shadow**，也不允許模型切換。

## 16. Promotion

本次不建議 MVEWMA→GARCH promotion，也不建議 routing promotion。
雖然全域 GARCH 在大規模樣本內 fixtures 表現較佳，但真正 point-in-time
與獨立 500 日證據缺失；不能用大量重疊的合成 workload 取代時間上的
獨立性。

## 17. 效能與後續 runner 優化

原正式 run 為單程序，耗時 3,001.32 秒。後續 runner 已新增：

- `--workers 8` 的 deterministic workload sharding。
- 多程序 `ProcessPoolExecutor`，對應 Ryzen 7 5700X 的 8 cores。
- 每 shard CSV/JSON checkpoint。
- `--resume` 跳過已完成 shard。
- deterministic sorted merge。

建議後續命令：

```bash
PYTHONPATH=python/risk_worker/src \
/private/tmp/equitylens-garch-fhs-venv/bin/python \
-m risk_worker.research.workload_routing_validation \
--input /private/tmp/equitylens-adjusted-close/phase5-five-year-input.json \
--output docs/workload-routing-validation \
--workers 8 --resume
```

優化不改模型公式、5,000 paths、seed、manifest 或評分。Sharding/merge
已有 deterministic unit tests；本次不重跑完整 50 分鐘資料來覆蓋已驗證
正式輸出。

## 18. 修改與產出

研究程式：

- `workload_routing_validation.py`
- `workload_routing_analysis.py`
- `routing_safety_validation.py`
- `test_workload_routing_validation.py`

所有 CSV/JSON、manifest、blocked design、forward/shadow spec、execution
log、tests、hash 與本報告均位於 `docs/workload-routing-validation/`。

## 19. Repository / PostgreSQL

PostgreSQL 最終狀態為 `exited`，FinishedAt
`2026-07-26T13:48:32.856653306Z`。最終 branch、HEAD 與 clean status
於提交後回報。
