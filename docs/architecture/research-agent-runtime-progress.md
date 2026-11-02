# Research Agent Runtime v4 — 實作進度

> 對應設計：[`research-agent-runtime.md`](research-agent-runtime.md)。驗收以設計文件開頭的「任務簡報」A1–A17 為準。
> 實作者每完成一個項目就更新本檔並 commit。Reviewer 早上只看這一份就能知道做到哪裡。

## 開工基準

| 項目 | 值 |
|---|---|
| 開工 commit | `8521e68`（設計分支） |
| 開工時 `dotnet test` 通過數 / 總數 | 861 / 873；12 項 PostgreSQL 尚未啟用，隔離 DB 啟用後全部執行 |
| 開工時 `evals` `pytest` 通過數 | 60 |
| 分支 | `feat/research-agent-runtime` |

## 驗收項目狀態

狀態：⬜ 未開始 ・ 🟨 進行中 ・ ✅ 完成 ・ ⛔ 阻塞 ・ ⏭ 略過（時間不足）

| # | 驗收條件（摘要） | 優先 | 狀態 | 證明的測試 | Commit | 備註 |
|---|---|---|---|---|---|---|
| A1 | 租約被他人持有時不 ack | MUST | ✅ | AgentRunWorkerTests | Phase 1 | lease lost 亦不 ack |
| A2 | 執行中斷的節點可被接手，run 不失敗 | MUST | ✅ | AgentExecutorBoundaryTests | Phase 1 | 含中斷預算耗盡 |
| A3 | 租約續約；失去租約者無法寫入 | MUST | ✅ | AgentLeasePostgresTests | Phase 1 | 真實 PostgreSQL，5 個測試 |
| A4 | AgentLoop 引擎不呼叫 graph planner；預設仍 v1 | MUST | ✅ | ResearchAgentWorkflowTests.Engine / ForbiddenPlanner | Phase 2–4 | |
| A5 | LLM 自主選工具；同輪平行；每次呼叫有紀錄 | MUST | ✅ | ResearchAgentLoopTests.ToolCalls_RunConcurrently | Phase 2–4 | |
| A6 | 違規呼叫被拒絕、記錄、回饋 | MUST | ✅ | GuardDenial / ToolBudget / SourcePolicy tests | Phase 2–4 | |
| A7 | 中斷後從下一輪續跑，不重做工具 | MUST | ✅ | CrashAfterCommittedTurn / CrashWithinTurn | Phase 2–4 | |
| A8 | 預算／無進展／格式錯誤時優雅停止 | MUST | ✅ | ProtocolFailure / TokenBudget tests | Phase 2–4 | |
| A9 | 輸出與 v1 相容（Critic、harness 不改） | MUST | ✅ | FixedWorkflow end-to-end tests | Phase 2–4 | |
| A10 | 確定性關卡攔截 → 修訂一次 → 仍失敗則拒答 | MUST | ✅ | FixedWorkflow revision / OutputGate / NumberGate tests | Phase 2–4 | |
| A11 | 每輪 LLM 決策可查詢 | MUST | ✅ | TwoTurns / detail LlmTurns tests | Phase 2–4 | |
| A12 | harness `--engine` 與軌跡指標；舊檔可 rescore | SHOULD | ✅ | evals/tests/test_trajectory.py | Phase 2–4 | |
| A13 | 副作用工具審批（參數綁定、冪等、Reject 附理由） | SHOULD | ✅ | ToolApprovalTests / ToolApprovalPostgresTests | Phase 4 | 測試工具；正式工具仍唯讀 |
| A14 | OTel span 與 metrics | SHOULD | ✅ | Telemetry_ParentsParallelToolsToTurn | Phase 2–4 | |
| A15 | 依風險觸發人工審閱 | COULD | ⬜ | | | |
| A16 | 實際 A/B 報告 | COULD | ⬜ | | | |
| — | 合併為單一 `QualityGate`、DAG 縮為約 6 節點 | COULD | ⬜ | | | |
| A17 | build / test / migration / pytest 全綠 | 每次 commit | ⬜ | | | |

## 每次 commit 的驗證紀錄

| 時間 | 完成項目 | `dotnet build` | `dotnet test`（通過/總數） | `has-pending-model-changes` | `pytest` |
|---|---|---|---|---|---|
| 2026-11-02 | A1–A3 | 通過 | 885 / 885，0 skipped | 無 pending | 60 通過 |
| 2026-11-02 | A4–A12、A14 | 通過（test build） | 915 / 915，0 skipped | 無 pending；隔離 DB migration 成功 | 69 通過 |
| 2026-11-02 | A13 / 數字 gate 實測修正 | 通過 | 924 / 924，0 skipped | 無 pending；隔離 DB migration 成功 | 69 通過 |

## Migration 清單

| 名稱 | 內容 |
|---|---|
| AddAgentRunLeaseFencing | lease_version、interruption_count |
| AddResearchLoopPersistence | LLM turns、checkpoint、tool turn FK / guard |
| ScopeResearchLoopCheckpointToAttempt | retry attempt 隔離及唯一鍵 |
| AddToolCallApprovals | scope / tool / arguments hash / idempotency key / lease version |

## 設計偏差

- Checkpoint 每個 node / execution attempt 保留最新一筆；輪次及工具歷史保留於獨立表。已落盤成功／失敗工具不重跑；未落盤的唯讀呼叫可重跑，不宣稱 exactly-once。
- 工具目前僅 search_local、search_web、get_document_page；沒有可信結構化 metric ledger，因此不開放 compute_growth 或原始數值計算參數。
- 固定 DAG 保留 16 個節點，完整重用 Critic；修訂後再次執行四個 Critic 節點。QualityGate 合併列為後續。
- Loop prompt 固定 template ID / version / hash；未接入可編輯 prompt 管理。
- 數值追溯直接檢查答案同句引用（NFKC、千分位、單位縮放、0.5% 相對容忍）；超過半數無依據則 blocking。沒有額外相信模型自報 claims。
- 成本與工具上限目前按 loop node 計算，Research 和 Revise 各有預算；不等於整個 workflow 帳單硬上限。
- OTel 是觀測性訊號；當機可能少計或重計停止事件，稽核以資料庫為準。

## 阻塞

| 項目 | 原因 | 已嘗試 | 建議 |
|---|---|---|---|
| | | | |

## 早上給 Reviewer 的摘要

（實作結束前填寫：完成了什麼、沒完成什麼、已知風險、建議下一步）

## 執行紀錄（2026-11-02 夜間）

- 交付點：2026-11-03 10:00 Asia/Taipei；外部 API 上限 US$10，目前已花 US$0。
- 分支 `feat/research-agent-runtime`；續跑 automation `research-runtime` 已建立。
- 隔離服務：`/tmp/equitylens-runtime-compose.yml`，Compose project `equitylens-runtime`；Postgres localhost:55432 / DB equitylens_runtime，Redis localhost:56379。只使用隔離 DB 做 migration／整合測試，正式服務未修改。
- Python venv：`/tmp/equitylens-runtime-venv`。原本 root 擁有的 bin/obj 已移至 `/tmp/equitylens-before-runtime-*`，重新建置成功。
- Phase 1：execution outcome 控制 ack；新 lease version；所有 executor DbContext 寫入有交易式租約 fencing；獨立 scope heartbeat；中斷節點有限次接手；取消與租約遺失不寫失敗結果。
- 已新增真實 PostgreSQL 測試：雙連線舊 worker 結果／tool 寫入全拒絕、過期拒寫及拒續約、續約不影響正常結果提交、競爭版本只有一方成功、超過原始租期仍可續約。
- 額外修正：既有 RiskObservabilityTests 跨執行緒 callback 使用 List 造成偶發遺失事件，改為 ConcurrentBag，原斷言不變。
- 驗證指令：`EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING` 指向上述隔離 DB，再執行完整 dotnet test。詳細終端紀錄 `/tmp/equitylens-phase1-tests.log`（不提交）。
- 下一步：Agent Loop 持久化契約、工具 guard／budget／ledger／checkpoint，接固定 ResearchAgent workflow，擴充 eval，再做真實 A/B。

- Phase 2–4：固定 ResearchAgent workflow、JSON 行動協定、同輪唯讀工具平行與逐筆落盤、輸出 adapter、Gate → 修訂 → 第二次 Critic、detail API LlmTurns、eval engine/trajectory、OTel spans/metrics 已實作。
- 隔離公開 corpus：74 securities、241 documents、8363 chunks / embeddings；uploaded user 欄位清空，無正式使用者或 portfolio 資料。

- Phase 4：副作用工具透過 RequireApproval 暫停；審批復用 `/api/agent-runs/{runId}/approvals/{approvalId}/approve|reject`，Rejected 亦恢復迴圈並回饋理由。參數 canonical hash、可見性、attempt、預算、24 小時到期重新驗證。最多一個副作用審批／輪；測試工具不註冊到正式 DI。
- 故障注入：副作用成功但結果尚未落盤就取消；接手後重用相同 idempotency key，測試下游只產生一次效果。這是下游冪等合約，不是跨系統 exactly-once 承諾。
- 真實 PostgreSQL 雙 reviewer 競爭：只有一個決策成功且只有一筆 wake outbox。
- 首筆實測 runtime 成功，但數字關卡誤擋正確 EPS；已修復句號後引用與表格單位欄名，新增回歸。首筆保守估算約 US$0.02（非供應商帳單）。

- 第一輪 A/B（commit `352eca9`）：DynamicDag 8/11，AgentLoop 5/11。結果保留在 `docs/eval-verification/2026-11-02-agent-loop-ab/{dynamic-dag,agent-loop}`；新引擎未達切換條件。
- 發現：24 筆證據片段導致 UTF-8 保守預留過大，起草前就拒答；改為依剩餘預算縮小 evidence window，保留完整 ledger，prompt version 升為 2。
- 發現：既有 Critic 的 JSON 被截斷，四題因此 runtime failure。供應商目前預設 thinking；加入 nullable `DeepSeek:ThinkingEnabled`，預設不改舊行為，第二輪隔離 A/B 兩邊都明確設 false。Critic handler 未改。
- 加入 provider 層 token / finish_reason 日誌以涵蓋 Critic 等未納入 run totals 的呼叫；不得把 run totals 當完整帳單。
- 額外邊界：明確非冪等 Running 節點不自動重跑；LocalThenWeb 修訂保留已完成但無結果的 local search 狀態。
- 此階段驗證：930/930 後端測試全通過；舊 11 題 responses 離線 rescore 仍為 9/11，未改舊題評分結果。

- 第二輪同設定 A/B（`11ce402`，thinking=false）：DynamicDag 7/11、AgentLoop 9/11，皆 0 runtime error / Web policy violation。v4 p95 30.162s，v1 10.733s，未通過 1.3 倍延遲門檻，因此預設仍不切換。完整 provider usage 的未快取尖峰上界：v1 US$0.0690、v4 US$0.0744，尚未含 embedding。
- 人工抽查發現 scorer PASS 的拒答附帶錯誤貨幣單位（億元與十億元）；加入仟元換算與「任何無法追溯的貨幣數值即 blocking」，prompt v3 要求無法直接回答則 give_up。一般非貨幣數字仍使用原本超過半數無依據規則。
- 新增 trajectory 的 guard_denial_rate / budget_exhausted_rate 與比較表。最新驗證：932/932 後端、70 pytest 通過。下一輪固定其餘設定驗證上述修正，不覆寫前兩輪。
