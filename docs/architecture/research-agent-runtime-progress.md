# Research Agent Runtime v4 — 實作進度

> 對應設計：[`research-agent-runtime.md`](research-agent-runtime.md)。
> 實作者每完成一個工作項目就更新本檔並 commit。Reviewer 早上只看這一份就能知道做到哪裡。

## 開工基準

| 項目 | 值 |
|---|---|
| 開工 commit | |
| 開工時 `dotnet test` 通過數 / 總數 | |
| 開工時 `evals` `pytest` 通過數 | |
| 分支 | `feat/research-agent-runtime` |

## 工作項目狀態

狀態：⬜ 未開始 ・ 🟨 進行中 ・ ✅ 完成 ・ ⛔ 阻塞 ・ ⏭ 略過（時間不足）

| 優先 | 項目 | 狀態 | Commit | 測試（新增數） | 備註 |
|---|---|---|---|---|---|
| 1 | Phase 1.1 Worker ack 修正 | ⬜ | | | |
| 1 | Phase 1.2 租約 heartbeat / fencing / TimeProvider | ⬜ | | | |
| 1 | Phase 1.3 中斷節點接手 | ⬜ | | | |
| 2 | Phase 2 工具 / Guard / Budget / Ledger | ⬜ | | | |
| 2 | Phase 2 Loop + 行動協定 + 平行 dispatch | ⬜ | | | |
| 2 | Phase 2 Checkpoint + `AgentLlmTurn` + 恢復 | ⬜ | | | |
| 3 | Phase 3 `ResearchAgent` workflow + catalog + DI | ⬜ | | | |
| 3 | Phase 3 OutputGate + RouteAfterQualityGate | ⬜ | | | |
| 3 | Phase 3 引擎切換（`Engine` / Options） | ⬜ | | | |
| 4 | Phase 5.1 Harness trajectory + `--engine` | ⬜ | | | |
| 5 | Phase 4 A 工具審批 | ⬜ | | | |
| 6 | OTel span / metrics | ⬜ | | | |
| 7 | Phase 4 C 結果審閱 | ⬜ | | | |
| 8 | Phase 5.2 實際 A/B | ⬜ | | | |
| 9 | Phase 4 B / 前端 | ⬜ | | | |

## 每次 Phase 結束的驗證紀錄

| 時間 | Phase | `dotnet build` | `dotnet test`（通過/總數） | `has-pending-model-changes` | `pytest` |
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
