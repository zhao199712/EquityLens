# EquityLens 風險研究與驗證文件

本頁集中列出既有 PR 的研究報告、資料版本脈絡與 2026-11-06 起的後續研究。已在 `docs/` 的文件保留原位置，只建立導覽，不重複複製或覆寫歷史報告。

整理基線：[main `c46a6af`](https://github.com/zhao199712/EquityLens/commit/c46a6af9a0ba98083762387862d68d415e945cd6)。PR 狀態保留 2026-11-06 首次整理時的查核紀錄；2026-11-07 僅追加數值研究報告。

## 先讀什麼

1. [2026-11-06 起的研究目錄與版本關係](research/2026-11-06/README.md)：原九份報告及 2026-11-07 新增的四模型 v4 數值研究，共十份。
2. [有限樣本校準研究](research/2026-11-06/calibration_report_20261106.md)：區分正確條件分布下的檢定大小，與 rolling/refit 流程的實務拒絕率。
3. [Long9 solver v2](research/2026-11-06/EquityLens_long9_solver_v2_report_20261106.md)：完整模型網格；[同 756 日模型比較](research/2026-11-06/EquityLens_matched756_asymmetry_report_20261106.md)與[條件狀態診斷](research/2026-11-06/EquityLens_conditional_states_report_20261106.md)是後續探索，不是新的獨立驗證集。
4. [四模型 v4：ES-CAViaR 數值驗證](research/2026-11-06/EquityLens_four_models_v4_numerical_validation_report_20261107.md)（2026-11-07）：局部 QP 取得精確停止證書，但固定點集的梯度 witness 仍未通過。完整 fit、12 個保留測試窗與金融 OOS 評分均未執行，不能解讀為 A／B／C／D 模型勝負。

## 前幾個 PR 的研究報告

### PR 29：ES、EVT 與 VaR 歸因

來源：[PR #29](https://github.com/zhao199712/EquityLens/pull/29)，已合併至上述基線。四份報告均已在 `docs/risk/validation/`，本次保留原文與原有更正告示。

| 報告 | 研究範圍 | 閱讀提醒 |
|---|---|---|
| [ES 回測（修訂版）](validation/es-backtest-report.md) | seed v1、48 檔、ES 與違約群聚診斷 | 必須先讀開頭撤回的 ES 低估結論及量尺修訂。 |
| [FHS GPD 尾部延伸](validation/fhs-evt-tail-report.md) | 與 v1 基準配對的 EVT challenger | Z1 驗收部分不可沿用；保留更正後的 QL／FZ0 解讀。 |
| [長歷史 seed v3：ES／EVT](validation/history-v3-es-evt-report.md) | long9／top10、快照與等權 | v3 取代舊 v2；資料缺價口徑與 production 不同。 |
| [VaR 頻率與違約集中歸因](validation/var-attribution-report.md) | 有限情境分位數、可交換虛無及逐項歸因 | 作為當時研究紀錄；rolling/refit 與後續校準限制另見 2026-11-06 報告。 |

[PR 29 合併時的固定版本目錄](https://github.com/zhao199712/EquityLens/tree/c46a6af9a0ba98083762387862d68d415e945cd6/docs/risk/validation)可用來核對歷史內容。較新的研究收窄了部分解讀，不代表舊文中較強的措辭已獲得新的驗證，也不把不同資料／樣本長度的數字合併排名。

### PR 25、27、28：資料與歷史評估版本

這三個 PR 的產出是資料流程與評估說明，不另虛構為已完成的風險模型回測報告。

| 來源 | 已有 docs | 版本脈絡 |
|---|---|---|
| [PR #25](https://github.com/zhao199712/EquityLens/pull/25) | [Cloud seed 使用說明](../development/cloud-seed.md)；[v1 合併版本](https://github.com/zhao199712/EquityLens/blob/b9661a4a9b8d8dfffc4b31a6a99353d3bc809f6c/docs/development/cloud-seed.md) | v1 可重現資料、隔離匯入與 smoke；不等同模型品質驗證。 |
| [PR #27](https://github.com/zhao199712/EquityLens/pull/27) | [歷史 seed eval 說明](../testing/history-seed-eval.md)；[v2 合併版本](https://github.com/zhao199712/EquityLens/blob/9f48bfc67972e5a951b142d71b4084198c8d3686/docs/testing/history-seed-eval.md) | top10／long9 輸入與 snapshot／等權，並未在該 PR 重跑完整模型。 |
| [PR #28](https://github.com/zhao199712/EquityLens/pull/28) | [歷史 seed eval 說明](../testing/history-seed-eval.md)；[v3 合併版本](https://github.com/zhao199712/EquityLens/blob/bef8b218e9d15a1a65fd8e6d9442b33b4f4450ae/docs/testing/history-seed-eval.md) | v3 修正 0050 分割基準，研究起點限制為 2004；仍未認證全部歷史價格。 |

[PR #30](https://github.com/zhao199712/EquityLens/pull/30)在首次整理查核時仍開啟，內容為 leading-gap 修正與回歸測試，沒有新增獨立研究報告。新 release 重播報告引用其 parity 規格，不能據此稱 PR 30 已合併或部署。

## 更早的研究與規格

下列文件原本已在 `docs/`。保留其研究時點與當時模型狀態，不把歷史的 primary／challenger 名稱當成目前 production 設定。

- [五年回測報告](backtests/five-year-backtest-report.md)與[研究規格](backtests/five-year-backtest-spec.md)：2026-07-26，四模型與雙權重比較。
- [追加模型驗證](../additional-risk-model-validation/additional-risk-model-validation-report.md)：rolling stability、ranking、MCS、FZ0、集中度與重估敏感度。
- [Workload routing 驗證](../workload-routing-validation/workload-routing-validation-report.md)：2026-07-27，不支持正式 routing；缺乏獨立 holdout 與 point-in-time 資料的限制保留。
- [風險引擎驗證計畫](validation/risk-engine-validation-plan.md)。
- [Adjusted-close 規格](data-pipeline/adjusted-close-spec.md)與[驗證紀錄](data-pipeline/adjusted-close-validation.md)。

## 收錄及解讀邊界

- 2026-11-06 收錄九份報告，2026-11-07 追加一份 v4 數值報告；新增內容只包含報告文字及導覽。原始 seed、修復後行情、逐日資料、證據包、研究程式與執行環境均未隨本次整理上傳；既有報告原文不變。
- 報告保留原本的 snapshot 曝險、來源識別與雜湊。私有 release 的連結維持原存取權限，公開報告不代表可以讀取私有資料。
- 報告內的「沒有 push／PR／部署」描述各次研究完成時的狀態；本次文件整理不改寫當時紀錄。新增文件不會變更生產模型、資料庫或驗收門檻。
- FZ0 的相對改善不等同絕對校準；不拒絕不等同模型正確。左右因子情境不是獨立實驗，也不是價格真值或上下界。
- 本目錄的索引和版本核對不構成重新執行研究或 repository-wide 測試。
