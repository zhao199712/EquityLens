# Research Agent Runtime v4 — 實作進度

> 對應設計：[`research-agent-runtime.md`](research-agent-runtime.md)。驗收以設計文件開頭的「任務簡報」A1–A17 為準。
> 實作者每完成一個項目就更新本檔並 commit。Reviewer 早上只看這一份就能知道做到哪裡。

## 開工基準

| 項目 | 值 |
|---|---|
| 開工 commit | |
| 開工時 `dotnet test` 通過數 / 總數 | |
| 開工時 `evals` `pytest` 通過數 | |
| 分支 | `feat/research-agent-runtime` |

## 驗收項目狀態

狀態：⬜ 未開始 ・ 🟨 進行中 ・ ✅ 完成 ・ ⛔ 阻塞 ・ ⏭ 略過（時間不足）

| # | 驗收條件（摘要） | 優先 | 狀態 | 證明的測試 | Commit | 備註 |
|---|---|---|---|---|---|---|
| A1 | 租約被他人持有時不 ack | MUST | ⬜ | | | |
| A2 | 執行中斷的節點可被接手，run 不失敗 | MUST | ⬜ | | | |
| A3 | 租約續約；失去租約者無法寫入 | MUST | ⬜ | | | |
| A4 | AgentLoop 引擎不呼叫 graph planner；預設仍 v1 | MUST | ⬜ | | | |
| A5 | LLM 自主選工具；同輪平行；每次呼叫有紀錄 | MUST | ⬜ | | | |
| A6 | 違規呼叫被拒絕、記錄、回饋 | MUST | ⬜ | | | |
| A7 | 中斷後從下一輪續跑，不重做工具 | MUST | ⬜ | | | |
| A8 | 預算／無進展／格式錯誤時優雅停止 | MUST | ⬜ | | | |
| A9 | 輸出與 v1 相容（Critic、harness 不改） | MUST | ⬜ | | | |
| A10 | 確定性關卡攔截 → 修訂一次 → 仍失敗則拒答 | MUST | ⬜ | | | |
| A11 | 每輪 LLM 決策可查詢 | MUST | ⬜ | | | |
| A12 | harness `--engine` 與軌跡指標；舊檔可 rescore | SHOULD | ⬜ | | | |
| A13 | 副作用工具審批（參數綁定、冪等、Reject 附理由） | SHOULD | ⬜ | | | |
| A14 | OTel span 與 metrics | SHOULD | ⬜ | | | |
| A15 | 依風險觸發人工審閱 | COULD | ⬜ | | | |
| A16 | 實際 A/B 報告 | COULD | ⬜ | | | |
| — | 合併為單一 `QualityGate`、DAG 縮為約 6 節點 | COULD | ⬜ | | | |
| A17 | build / test / migration / pytest 全綠 | 每次 commit | ⬜ | | | |

## 每次 commit 的驗證紀錄

| 時間 | 完成項目 | `dotnet build` | `dotnet test`（通過/總數） | `has-pending-model-changes` | `pytest` |
|---|---|---|---|---|---|
| | | | | | |

## Migration 清單

| 名稱 | 內容 |
|---|---|
| | |

## 設計偏差

（同步記錄到設計文件 §15）

## 阻塞

| 項目 | 原因 | 已嘗試 | 建議 |
|---|---|---|---|
| | | | |

## 早上給 Reviewer 的摘要

（實作結束前填寫：完成了什麼、沒完成什麼、已知風險、建議下一步）
