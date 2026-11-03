# 移除動態 DAG — 實作進度

> 對應規格：[`remove-dynamic-dag.md`](remove-dynamic-dag.md)。驗收以規格 §5 的 R1–R14 為準。

## 開工基準

| 項目 | 值 |
|---|---|
| 開工 commit | |
| 開工時 `dotnet test` 通過數 / 總數 | |
| 開工時 `evals` `pytest` 通過數 | |
| `Services/Agents` 開工時行數 | |
| 分支 | `refactor/remove-dynamic-dag` |

## 驗收項目狀態

狀態：⬜ 未開始 ・ 🟨 進行中 ・ ✅ 完成 ・ ⛔ 阻塞 ・ ⏭ 略過

| # | 驗收條件（摘要） | 優先 | 狀態 | 證明 | Commit | 備註 |
|---|---|---|---|---|---|---|
| R1 | 動態 DAG 型別零參照 | MUST | ⬜ | | | |
| R2 | 研究一律 ResearchAgent；DynamicDag → 400 | MUST | ⬜ | | | |
| R3 | 回饋修訂併入 Agent 迴圈 | MUST | ⬜ | | | |
| R4 | 研究品質審查改固定 DAG | MUST | ⬜ | | | |
| R5 | 投組診斷 Agent 迴圈 + Compute 工具 | MUST | ⬜ | | | |
| R6 | 投組診斷人工審批不變 | MUST | ⬜ | | | |
| R7 | 投組迴圈可觀測、可續跑、重用基礎設施 | MUST | ⬜ | | | |
| R8 | 舊 run 唯讀、retry 409、佇列安全失敗 | MUST | ⬜ | | | |
| R9 | 使用者隔離 | MUST | ⬜ | | | |
| R10 | Admin 介面與前端 | SHOULD | ⬜ | | | |
| R11 | Eval harness 更新 | SHOULD | ⬜ | | | |
| R12 | 文件更新與前後對照 | SHOULD | ⬜ | | | |
| R13 | 8 題 eval 不退步 | COULD | ⬜ | | | |
| R14 | build / test / migration / pytest 全綠 | 每次 commit | ⬜ | | | |

## 驗證紀錄

| 時間 | 完成項目 | `dotnet build` | `dotnet test`（通過/總數） | `has-pending-model-changes` | `pytest` | 前端 build/test |
|---|---|---|---|---|---|---|
| | | | | | | |

## 刪除清單

| 檔案 | 行數 | 理由 |
|---|---|---|
| | | |

## 阻塞

| 項目 | 原因 | 已嘗試 | 建議 |
|---|---|---|---|
| | | | |

## 早上給 Reviewer 的摘要

（結束前填寫：完成了什麼、沒完成什麼、已知風險、移除前後對照數字、建議下一步）
