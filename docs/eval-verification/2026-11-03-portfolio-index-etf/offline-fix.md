# 比較方向修正的離線驗證

日期：2026-11-03。修正基準 `856b1a5`，loop prompt 19、portfolio method v4。本文件是修正驗證，原 README／answer／summary／responses 仍保留 c22268e 的真實模型結果。

## 重現與修正

保存回答的數字 gate 原本回傳空問題清單。新增 `SavedLiveAnswer_RejectsReversedVarComparisonDespiteCorrectNumbers` 後，修正前 1 failed（預期 `ScenarioComparisonDirectionMismatch`，實際沒有）。修後，同一份原始答案會被擋，將 VaR 與 ES 分開敘述且改正 VaR 方向後通過。

工具依同一 baseline 確定性產生各情境逐項改善比較，並提供模型與最終摘要使用。明示的大小／改善比較必須對應引用的工具結果；不明主詞、多同類情境、來源不足或未支援的否定形式回修訂，不能猜測。波動較低有利，帶符號 VaR／ES／回撤較高有利；保留相等與惡化情形。規則只覆蓋明示的中文關係語法，不是任意自然語言判斷器。

模型只獲既有的一次修訂；測試中在要求修訂後中斷，恢復時沿用已保存工具與 checkpoint，沒有重複工具。若仍失敗，最終 gate 標示 Limited、移除問題段落，保留可驗證的偏好說明及確定性比較摘要。UI 可顯示比較方向錯誤／依據不明。

## 最終驗證

- 後端 build、**1067 passed／0 skipped**，含 **18 真實 PostgreSQL 整合測試**；TRX 驗證器確認測試未略過。
- 新增 **21** 案例，涵蓋原文、正確版本、四指標方向、數值大小／改善幅度區別、共同負值、平手／惡化、模糊主詞／引用／多方案、數值目標、修訂恢復、最終段落處理。
- 原 round-19 的 14 題中，10 份模型報告沒有新比較誤擋；4 題是澄清而沒有報告。只檢查新規則，不更改歷史 scorer 或成績。
- 前端 **138** 測試、production build、固定 API Playwright smoke **10** 通過。
- Eval harness **92** 通過，EF 無 pending-model changes，git diff --check 通過。
- pytest 首次誤用虛擬環境原 worktree 的 editable package，4 項失敗；明確設定本分支 PYTHONPATH 後 92 通過，沒有改評分器。

本輪沒有付費模型呼叫。真實 AI 是否能穩定利用新比較依據，仍須另外的授權 eval；不以離線測試宣稱新的模型分數。未合併 main、未部署、未修改正式資料。
