# Workload routing validation：Task 0

日期：2026-07-26

1. Branch：`python`；開跑前 HEAD：
   `796be3263c298a0a011c1fa8a8d93a25460dad87`。
2. Adjusted-close panel：
   - Universe：2330、2454、2308、2317、3711、2303、2383、2891、
     2344、2345、2881、2882、1303、2382、2887、2360、3017、
     2885、2412、2886。
   - 價格期間：2021-07-01–2026-07-16。
   - 同步有效 returns：1,220；252 日 lookback 後 OOS：968。
   - Factor version：`tw0050-total-return-v1-20260726`。
3. 固定模型設定：lookback=252、refit=21、simulations=5,000、
   confidence=95%/99%；seed 為 returns matrix 與完整 20-slot weights
   的 float64 bytes SHA-256，再由既有 `stable_seed` 加 prediction
   offset。兩模型於同一 workload 使用相同資料、日期與 derivation。
4. 這五年資料已參與 workload-dependence 假說形成，故本次結果全部標記
   in-sample robustness，不冒充獨立 holdout。
5. 獨立驗證資料盤點：
   - 2026-07-16 之後尚無已完成相同 factor-version 驗證的新共同交易日。
   - Repo 無另一個未參與原研究且具有五年、同品質 adjusted-close 的
     universe。
   - Repo 無歷史 0050 point-in-time 成分、當時權重與真實再平衡資料表。
   因此本次沒有真正獨立資料。
6. 事前候選規則：
   - A：全域 MVEWMA。
   - B：全域 VT-GARCH。
   - C：HHI < .10 使用 GARCH，否則 MVEWMA。
   - D：HHI < .10 且 N_eff >= 10 使用 GARCH，否則 MVEWMA。
   - E：HHI < .10 且 first-PC < .60 使用 GARCH；只有 Task 2 顯示
     first-PC 有穩定增量解釋力時才具 routing eligibility。
7. `routing_rule_manifest.json` 已在執行前建立；SHA-256：
   `90b328d5d9b07aaa2bb7a61c836f2db0605ed155f5fdc30649f5f2a4d9f0cf6f`。
8. 自此不得修改 manifest、門檻、抽樣 seed 或規則；執行後會再次驗 hash。

