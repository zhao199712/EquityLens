# GARCH 與 GJR 群聚條件診斷

## 結論先行

最值得優先研究的是「過去五個交易日投組累積報酬為負」的狀態，而不是單日 shock2。97.5% 主分析中，negative5 的違約率差與標準化聯合殘差差在全部八個模型／情境／權重格都是正值；但統計穩健程度不一，不能把全部八格都稱為顯著。

等權 GARCH 的 negative5 違約率差，在左右兩個價格情境、21／63 日兩種 block 下，Holm 校正與保守的同時 Monte Carlo 精度上界均低於 .05。這是本輪最一致的探索性關聯。左右情境是同一資料的兩個假設，絕不是兩次獨立重現。GJR 的左情境等權違約率差、snapshot GARCH 的聯合殘差差，雖兩種 block 的 Holm 點估 p 都低於 .05，21 日 block 的 Monte Carlo 同時上界仍跨 .05，須保留邊界不確定。

shock2 沒有通過本次多重比較的穩定證據。這不代表 shock 沒影響，也不代表模型合格；不能用本輪結果預設「快慢波動一定能修好」。下一步若要開發候選模型，較合理的是先研究持續下跌期間的尾部反應，再比較快慢波動、非對稱更新或其他機制。這是研究優先順序，不是生產模型選擇。

## 研究範圍與先行凍結

- 使用 long9 v2 已有 GARCH-FHS 與 GJR-FHS、756 日視窗、21 日重估預測，沒有重估任何模型、沒有改舊成果或生產。
- 2007-01-18 至 2026-11-04，共 4,860 個共同 OOS 日；long9 排除 3711。snapshot／equal、left／right、95%／97.5%／99% 全列，只有 97.5% 做本輪推論。
- protocol.json 在條件結果產生前凍結，SHA256 為 a5004e511288928cfb7d5259b3587e43bb6a4f64d0958784c831b5c9cc1adfb1。公式、時間索引與獨立設計審查完成後才啟動；run_start.json 事前記錄 seed base+block length。
- 歷史已在先前研究看過；本輪是 post-hoc 探索，絕非盲樣本或最終驗證。
- 每情境仍有 11 筆價格調整因子假設，以及倖存偏誤、非歷史時點成分股與權重限制。feature「無前視」只表示在這份固定 panel 中使用正確時間索引，不能證明底層價格假設或 snapshot 權重是歷史當下可取得。

## 五個固定狀態

1. lag_hit：同模型、同 level 的前一交易日 VaR 違約。首日沒有前一日 forecast，保留 NA，因此有效日數 4,859。兩模型的 on 群不同，不是共同條件下的配對模型優劣比較。
2. negative5：t−5 至 t−1 的投組簡單日報酬複利 prod(1+r)−1 < 0。投組每日採原研究固定權重，是每日再平衡報酬。
3. shock2：前日絕對投組報酬／再前 20 日 RMS > 2。分母明確使用 t−21 至 t−2，不包含 shock 日 t−1；分母零則 missing。
4. breadth6：前日九股中至少六股下跌。
5. late_refit：確定的重估排程 phase 14..20，對照 0..13；不是用當日 actual 判斷。

其餘四個狀態在同情境／權重下對兩模型一致，並對三個 level 一致。沒有尋優門檻、沒有試狀態交集。完整歷史 warmup 保留共享狀態 4,860 日。獨立測試重建 19,440 個共享特徵日、檢查 116,616 個 lag-hit 關係，並做 32 組 future-return perturbation 與 prefix invariance，全部通過。

## 指標與 ES 的正確解讀

所有 source manifest 確認 ES 是 fixed-alpha integrated quantile，含 fractional boundary mass；全日 e≤q<0、有限值均通過。違約指標遵照舊成果為 y<q。離散 FHS／nearest-rank VaR 可在分位數邊界有機率質量，因此即使分布模型正確，嚴格 y<q 的機率也可低於名目 alpha 且隨日變動；本輪 hit on−off 是觀測關聯，不自動構成精確校準檢定。搭配 fractional-boundary ES 的 R 識別式仍可在邊界有機率質量時滿足均值零，因為邊界項 q−y=0。

每個 state on/off 同時列 N、違約數／率、率減名目 alpha、平均實現損失 −y、平均預測 ES 損失 −e、違約日平均損失與 H 平均。H=(alpha−PIT)/alpha·I(PIT≤alpha)，連續 uniform 參考均值 alpha/2；H 不是金額 ES 誤差，未產生 DE p 值。

另列 joint VaR/ES identification residual：

R=e−q+(q−y)·I(y≤q)/alpha。

R 報 decimal return 與 basis points，並報 R/(−e)。在正確條件 quantile／ES 識別下均值應為 0；正值傾向聯合尾部風險低估，但受到 VaR 錯配影響，不能稱為獨立純 ES 檢定。R 的 bp 是 residual 尺度，不是可直接當作 ES 少估金額的獨立估計。條件均值與 on−off 差都完整列出。

## 主要效果大小

以 left 情境為例（不是挑選唯一情境；完整左右結果都在 CSV）：

|權重／模型|negative5 off：N／違約率|on：N／違約率|on−off 率差|R/(−e) off → on|R bp off → on|
|---|---:|---:|---:|---:|---:|
|equal GARCH|2,840／1.831%|2,020／3.960%|+2.129 百分點|−0.0780 → 0.2020|−30.78 → 76.63|
|equal GJR|2,840／1.972%|2,020／3.713%|+1.741 百分點|−0.0596 → 0.1773|−21.24 → 67.42|
|snapshot GARCH|2,752／2.144%|2,108／3.510%|+1.367 百分點|−0.0846 → 0.2105|−33.75 → 82.32|
|snapshot GJR|2,752／2.507%|2,108／3.083%|+0.576 百分點|−0.0459 → 0.1606|−15.57 → 63.65|

名目違約率為 2.5%。這些表列是描述，不是各組對 2.5% 或 residual=0 的推論檢定。本次推論只檢定 on−off，不能因此宣稱 off 校準正確或 on 絕對不合格。也未正式檢定 GARCH 與 GJR 的效果差，不作模型勝負結論。

跨八格的 97.5% 狀態方向：

|狀態|違約率 on−off 範圍（百分點）|R/(−e) on−off 範圍|證據摘要|
|---|---:|---:|---|
|lag_hit|+1.051 至 +3.505|+0.486 至 +0.928|on 僅約百餘日，兩 block Holm 皆無通過；model-specific 分組|
|negative5|+0.574 至 +2.129|+0.206 至 +0.295|最一致，仍需細分權重、模型與 MC 精度|
|shock2|+0.313 至 +0.864|+0.048 至 +0.295|兩 block 所有 Holm p=1，不等同無效果|
|breadth6|+0.790 至 +1.820|+0.128 至 +0.271|GJR equal 違約率僅 63 日 block 點估通過，且 MC 邊界不穩定|
|late_refit|−0.981 至 −0.734|−0.162 至 −0.108|描述方向反而較低；沒有支持接近重估末期更差的穩定證據|

## 95% 群聚問題的描述連結

先前關心的群聚出現在 95% level，不能把本輪 97.5% 的推論結果外推成「95% 沒有群聚」。以 95% 等權 GJR 的 lag_hit 為例，左右情境數字相同：

- 前一日未違約（off）：N=4,592，當日違約 236 次，違約率 5.139%。
- 前一日已違約（on）：N=267，當日違約 31 次，違約率 11.610%。
- on−off 為 +6.471 百分點，首日仍因無前一日 forecast 排除，有效總日數 4,859。

這個例子只是銜接先前的 95% 群聚問題，沒有新增 bootstrap 檢定、p 值或多重比較家族。它描述「前一日違約後，當日違約率較高」，不能單憑此例斷言群聚成因、模型優劣或未來可重現程度；lag_hit 分組仍是 model-specific。95%／99% 全部狀態描述均保留在 CSV。

## 推論、稀疏樣本與模擬精度

對既有每日 (state,outcome) 列共同 circular moving-block bootstrap；兩模型、情境、權重共享日索引。分別 block=21、63，各固定 100,000 draws；seed 為 2026110622／2026110664。每次拼接至 4,860 日，尾 block 截斷。狀態標記不在人工 block 接縫重算，因此是既有預測／特徵的條件式診斷，不是完整 rolling/refit 流程校準。

兩個 outcome 為違約率差與平均 R/(−e) 差。每 block 各自固定 5 states×2 models×2 scenarios×2 weights×2 outcomes=80 個主比較，Holm 校正；不挑較有利 block。95/99 僅描述。95% CI 為 basic bootstrap、未做 simultaneous correction；兩側 p 以 centered bootstrap 加一計數估計。

事前 gate：原始 on/off 各至少 100 日；否則只描述，家族內 p=1。任何 bootstrap 零分母抽樣記 invalid 不補抽；若 valid<99,000 抑制推論。本次所有 160 列 eligible，全部每列 100,000 valid、零 invalid。gate 通過不消除尾部事件稀少的問題。

Holm 點估在 21 日 block 有 6 項、63 日有 8 項 <.05，共同 6 項全為 negative5。模擬精度另對全部 160 極端機率做同時 95% CP 矩形（每尾 .05/(2×160)），加一保守轉換後，逐 block 把完整 80 維上下界重新 Holm。這只是 Monte Carlo 精度，不是額外市場推論，也沒有增加 draws。

- 21 日 block：3 項保守上界仍 <.05，3 項邊界不確定，74 項保守下界 ≥.05。
- 63 日 block：6 項保守上界仍 <.05，5 項邊界不確定，69 項保守下界 ≥.05。
- 兩 block 都通過保守精度上界的共有 3 項：left/right 的 equal GARCH negative5 違約率差，及 right equal GJR negative5 違約率差。
- equal GARCH 左情境 Holm 點估 0.001600／0.000800，保守 MC 上界 0.009816／0.007811；右情境點估 0.002370／0.001580，上界 0.011385／0.009694。

不能將 p>.05 解讀為合格；也不能把 MC 精度穩定當成非平穩長歷史下 bootstrap 方法完全校準。

## 危機描述與研究方向

事前指定 2008、2020、2022 三個曆年作描述，完整所有年份一律提供，沒有另建危機門檻或加推論。left/equal 的 2008 違約率：GARCH 6.02%、GJR 5.22%；2022：5.28%、5.69%。2020 分別 2.86%、3.27%，但平均 R/(−e) 約 0.461／0.459，說明違約頻率與聯合尾部嚴重程度不是同一件事。每年樣本約 245–249 日、實際尾部事件更少，不應當成獨立穩健危機檢定。

五個單狀態各自分析，跌勢、shock、廣度與波動制度彼此重疊且混雜；沒有同時控制，不能識別群聚成因。建議下一輪先凍結能辨別「持續跌勢下尾部反應」的候選方案與新驗證方法，再比較快慢波動是否真有增益。若沒有新資料，仍需清楚標示開發與評估都受重複看歷史影響。不要根據這些 p 值直接挑 production。

更具體的後續建議是：先預先鎖定一個低參數、具下跌持續性或尾端回饋的直接動態 VaR/ES 候選，與 GJR756/21 對照；快慢波動保留為競爭機制，而非本輪已支持的修法。shock2 只是單一粗略代理，沒有通過本輪比較不排除快慢波動可能有效。上述只是研究建議，尚未建模、未回測，也不表示下一輪已獲准開跑。

## 交付與重現

完整 daily_features.csv 含特徵原始量、t−1／RMS窗口日期與 state、q/e/y/PIT/H/R，condition_tables.csv 與 condition_contrasts.csv 給全 level 的條件均值及差。inference_primary.csv、mc_precision_all160.csv 給每個固定主比較的完整結果，yearly_context.csv 給全年描述，manifest.json 與 protected_before_hashes.json 保存證據。

重現命令見 README.md。原始 bootstrap_block*.npz 約 102 MB，留本地不放精簡交付包；可由腳本、輸入、固定 seeds 與固定 draw 數完整重建。交付包應含完整 daily CSV、輸入價格／forecasts 與來源函式；沒有必要重跑模型。獨立審查已完整重算並通過：116,640 個日列、240 個條件列、120 個對比、480 個年度列，以及 160 項各 100,000 draws 的 basic CI、Holm80 與 MC 同時矩形均一致。審查證據另由審查報告提供。
