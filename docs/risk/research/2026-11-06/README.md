# 2026-11-06 起的風險研究報告

[返回風險研究總覽](../../README.md)

本目錄保留 2026-11-06 的九份報告，並於 2026-11-07 追加四模型 v4 數值研究，共十份報告，維持原文與數值。文件整理沒有重估模型、重跑研究、改動 production 或發布新 seed。

## 閱讀順序與版本關係

| 順序 | 報告 | 範圍與關係 |
|---|---|---|
| 1 | [Release 歷史重播](EquityLens_release_replay_report_20261106.md) | seed v3 原始輸入，統一 adjusted-price 口徑；保留舊報告與同環境受控基線差異。 |
| 2 | [Seed 局部修復與重播](EquityLens_seed_repair_report_20261106.md) | 局部修復資料；未解因子不補造，long9 另列 left／right 假設。 |
| 3 | [Top10 模型比較](EquityLens_model_comparison_report_20261106.md) | 1,317 個共同 OOS 日；35 設定中 33 完成，使用修正後 fixed-alpha ES。 |
| 4 | [有限樣本校準](calibration_report_20261106.md) | 450 條主模擬路徑與 10,000 條 iid VaR 虛無路徑；對齊 T=1,317 的指定單資產流程。 |
| 5 | [Long9 初版模型比較](EquityLens_long9_model_comparison_report_20261106.md) | 4,860 個共同 OOS 日；每情境 20/35 設定完成，保留數值失敗記錄。 |
| 6 | [Long9 solver v2 完整重播](EquityLens_long9_solver_v2_report_20261106.md) | 同資料與共同期間，數值修正後完成 140/140 組合序列；與初版並存，最新完整網格讀本篇。 |
| 7 | [同 756 日 GJR／GARCH 比較](EquityLens_matched756_asymmetry_report_20261106.md) | 從 v2 預測做同窗同頻配對；主設定 756/21，5／63 日重估為敏感度。 |
| 8 | [同 756 日尾部診斷](EquityLens_matched756_tail_diagnostics_report_20261106.md) | 既有預測的 VaR 違約群聚及累積 PIT 指標；沒有新造 ES 校準 p 值。 |
| 9 | [條件狀態診斷](EquityLens_conditional_states_report_20261106.md) | 756/21 既有預測與五個固定 lagged states；97.5% 主推論，其餘水準作描述。 |

順序是閱讀建議，不宣稱各研究彼此獨立。後四份使用相同 long9 歷史；solver v2 保留原參數域，但重新求解部分窗口，不能把初版與新版當成只補上失敗列。Top10 與 long9 的資產、權重、期間與重估相位不同，不能混成單一排名。

## 2026-11-07 後續：四模型 v4 數值驗證

[四模型 v4：近收斂精度進展與梯度證明終驗限制](EquityLens_four_models_v4_numerical_validation_report_20261107.md)承接條件狀態研究後的 A／B／C／D 比較計畫，但本輪只處理已知合成狀態的 ES-CAViaR 數值困難，不是新增金融 OOS 結果。

- 10 次數值 QP、3 次實際 FZ0 下降，以及同一已捨入係數 QP 的新有理數輔助停止證書，分開記錄。
- 原固定 witness 構造未取得候選；同一 792 點的 minimax 候選實際接受獨立終驗後，因 L2 殘差約 0.0143 遠高於原 rho=1e-6 而拒絕。
- 精確分離下界僅排除這組固定 binary64 梯度向量的合格凸組合，不能外推到整個 epsilon 鄰域或所有合法點集。
- 完整多起點 fit、12 個保留數值測試窗及金融 OOS 評分均未執行；四模型比較仍未完成。本輪已停止，後續選項尚未執行。

本次僅追加這篇 v4 報告，不含其提及的 v1–v3 報告、研究程式或證據包；上列九篇歷史報告原文不變。

## 必須保留的限制

- Long9 的 11 筆未解股票因子仍採明示左右假設；兩者不是已驗證真值、信賴區間、上下界或兩次獨立重現。
- Snapshot 是固定曝險，並非逐日歷史持倉；目前成分股、權重及 adjusted history 仍有倖存、回看及 point-in-time 限制。
- 單資產 T=1,317 的校準不能外推成 long9 T=4,860、多資產、GJR／EVT／DCC、FZ0 bootstrap 或條件狀態分析已全面校準。
- 相對 FZ0 改善、數值求解通過及 Monte Carlo 精度通過，各自回答不同問題，不能互換為模型已合格的證明。
- 模型比較與條件分析使用已看過的歷史，屬探索性研究。Negative5 的關聯不識別成因，也未驗證新的動態 VaR／ES 候選。
- PR 29 的 ES／EVT 舊報告含撤回與更正告示，請經由[總覽](../../README.md)連同版本脈絡閱讀。

## 報告與證據包的區別

本目錄僅上傳 Markdown 報告，不包含原始 seed、修復後價格、每日 CSV、bootstrap 陣列、來源程式或證據壓縮包。報告提及的 `README.md`、manifest、CSV、測試與重現命令，屬於各份原研究包；不是指這個文件目錄已提供完整可執行套件。

報告保留當時的驗證範圍與完成狀態。原文中的「沒有 push／PR／部署」描述研究當時狀態，不否定此次公開存檔。私有 release 連結與來源雜湊只用於追溯，並未改變原資料的權限。

## 原文完整性

以下 SHA-256 對應本次收錄的 UTF-8 檔案；十份報告各自與收錄時交付稿逐位元一致。索引另行撰寫，不改寫原報告的研究結論。

| 檔案 | SHA-256 |
|---|---|
| EquityLens_release_replay_report_20261106.md | b9d1174fd45d9e0b5810e98514a8b69d831da0ed55a11b6ec911d00426dae289 |
| EquityLens_seed_repair_report_20261106.md | 617eefe2e1fa31074d2971de28b4a215907634c8c54e8d48efd2fb72eeae4b6e |
| EquityLens_model_comparison_report_20261106.md | fa61155f6f3f99847bcbc040e40d9c6d2144f9be662935b9946d9704588529b7 |
| calibration_report_20261106.md | 15600900d740cf629e9d0c05d903c8451712bea871a335c62605a093afd3caa1 |
| EquityLens_long9_model_comparison_report_20261106.md | 6db71b60c224b1956056e94b38412f65a07157eda645230c7089d22ba928032d |
| EquityLens_long9_solver_v2_report_20261106.md | 1603bf283ccf7b70880c4621b56d9009819b93f1f506774b725a896790c6f79f |
| EquityLens_matched756_asymmetry_report_20261106.md | 4af4be7970e6f5b0f9b5f6d73231cd795b6a2934c2da1ce98eef2263e69b2e37 |
| EquityLens_matched756_tail_diagnostics_report_20261106.md | 80498c4575b1d52fcbb20e2a432269199f097497a4eeba2fbe619068a5bed752 |
| EquityLens_conditional_states_report_20261106.md | 0e88ae32714a9dc2eb16343815d88cabdb9d3bafced5bd2d7b3947936a3e8f04 |
| EquityLens_four_models_v4_numerical_validation_report_20261107.md | 18e7c8fc5ea830e7aa8143129acd0ea7ca651922f28d85406f4beda96af36cb7 |
