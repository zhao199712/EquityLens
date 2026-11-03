# 收斂為 3 個工作流 — 實作進度

> 對應規格：[`remove-dynamic-dag.md`](remove-dynamic-dag.md)。驗收以規格 §7 的 R1–R27 為準。

## 開工基準

| 項目 | 值 |
|---|---|
| 開工 commit | `8903e48` |
| 開工時 `dotnet test` 通過數 / 總數 | 最近基準 979/979；R1 後實跑 986/986，0 skipped |
| 開工時 `evals` `pytest` 通過數 | 75 |
| 開工時前端 `npm test` 通過數 | 124 |
| 可建立的工作流數 | 9 |
| catalog node type 數 | 54（新增情境節點已納入） |
| `Services/Agents` 行數 | 11,674 |
| 分支 | `refactor/three-workflows` |

## 驗收項目狀態

狀態：⬜ 未開始 ・ 🟨 進行中 ・ ✅ 完成 ・ ⛔ 阻塞 ・ ⏭ 略過 ・ 🔁 無法重現

### Part A：收斂架構

| # | 驗收條件（摘要） | 優先 | 狀態 | 證明 | Commit | 備註 |
|---|---|---|---|---|---|---|
| R1 | 舊資料唯讀、retry 409、佇列安全失敗 | MUST | 🟨 | WorkflowExecutionCompatibilityTests：7 passed；版本／節點辨識、四種非終態、租約、讀取／retry 隔離 | 本項提交 | 防護完成；隨後續 provider 更新啟用各舊版退役，R10 再總驗收 |
| R2 | 研究一律 ResearchAgent；DynamicDag → 400 | MUST | ⬜ | | | |
| R3 | qualityGate 合併 | MUST | ⬜ | | | |
| R4 | 回饋修訂併入 ResearchAgent | MUST | ⬜ | | | |
| R5 | 投組 Agent 迴圈與工具 | MUST | ⬜ | | | |
| R6 | 投組人工審批不變 | MUST | ⬜ | | | |
| R7 | 迴圈基礎設施共用、可續跑、可查詢 | MUST | ⬜ | | | |
| R8 | 刪除 4 個多餘工作流與前端呼叫 | MUST | ⬜ | | | |
| R9 | Ask Agent 路由到 3 個工作流 | MUST | ⬜ | | | |
| R10 | 移除型別零參照 | MUST | ⬜ | | | |
| R11 | 3 個工作流、node type ≤ 15 | MUST | ⬜ | | | |
| R12 | 使用者隔離 | MUST | ⬜ | | | |
| R13 | Admin 介面 | SHOULD | ⬜ | | | |
| R14 | Eval harness | SHOULD | ⬜ | | | |
| R15 | 文件與前後對照 | SHOULD | ⬜ | | | |

### Part B：實測問題

| # | 問題 | 優先 | 狀態 | 重現測試 | 修正測試 | Commit | 備註 |
|---|---|---|---|---|---|---|---|
| R16 | B1 OutputGate 誤判年份／代號 | MUST | ⬜ | | | | |
| R17 | B3 情境偷換＋澄清 | MUST | ⬜ | | | | |
| R18 | B2 優化建議 | MUST | ⬜ | | | | |
| R19 | B4 路由重試＋log | MUST | ⬜ | | | | |
| R20 | B8 拖累正負號 | SHOULD | ⬜ | | | | |
| R21 | B6 VaR 樣本不足 | SHOULD | ⬜ | | | | |
| R22 | B5 數字口徑 | SHOULD | ⬜ | | | | |
| R23 | B7 產業分類 | SHOULD | ⬜ | | | | |
| R24 | B9、B10 前端 | COULD | ⬜ | | | | |
| R25 | B11 log 等級 | COULD | ⬜ | | | | |

### 驗證

| # | 驗收條件 | 優先 | 狀態 | 證明 | Commit | 備註 |
|---|---|---|---|---|---|---|
| R26 | 8 題 eval 不退步 | SHOULD | ⬜ | | | |
| R27 | 全部驗證全綠 | 每次 commit | ✅ | R1 完整驗證通過 | 本項提交 | 每項重新執行 |

## 驗證紀錄

| 時間 | 完成項目 | `dotnet build` | `dotnet test`（通過/總數） | `has-pending-model-changes` | `pytest` | 前端 build/test |
|---|---|---|---|---|---|---|
| 2026-11-03 09:20 | R1 防護基礎 | PASS | 986/986，0 skipped | 無變更 | 75 passed | build PASS／124 passed |

## 刪除清單

| 檔案 | 行數 | 理由 |
|---|---|---|
| | | |

## 收斂前後對照（R15）

| 指標 | 收斂前 | 收斂後 |
|---|---|---|
| 可建立的工作流 | 9 | |
| catalog node type | 54 | |
| `Services/Agents` 行數 | 11,674 | |
| 編排模式 | Static／Stateful／DynamicStateful | |

## 阻塞

| 項目 | 原因 | 已嘗試 | 建議 |
|---|---|---|---|
| | | | |

## 給 Reviewer 的摘要

（結束前填寫：完成了什麼、沒完成什麼、收斂前後數字、已知風險、正式站問題處理結果、建議的 merge 與部署步驟）

## 實作紀錄

- R1：版本相容性判定注入正式 DI；執行、retry、回饋、兩種審批入口有防護。HTTP exception filter 將退役錯誤轉為 409。GET 保持既有 fallback，不要求歷史節點仍存在 catalog。
- worker 先取得租約及 fencing，再終止退役圖；WaitingForFeedback／WaitingForApproval 的舊圖同樣可安全終止，不執行 handler、不寫新的 wake。
- 按使用者補充，D3 已加入投組任務型工具上限 8 個、首批 6 個；底層數學函式不對模型公開。
- 無正式環境／DB 變更，無模型呼叫。R1 完整驗證記錄在本機 `/tmp/r1-*.log`；不把本機 log 納入 Git。
