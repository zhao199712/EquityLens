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
| A16 | 實際 A/B 報告 | COULD | ✅ | 限定公司 8 題與完整原始結果 | docs 階段 | 最新兩輪數值門檻達標；仍保留舊引擎預設 |
| — | 合併為單一 `QualityGate`、DAG 縮為約 6 節點 | COULD | ⬜ | | | |
| A17 | build / test / migration / pytest 全綠 | 每次 commit | ✅ | 958 backend / 75 pytest / 無 pending model | 53b5db1 / 379022b | 後續修改仍需再驗證 |

## 每次 commit 的驗證紀錄

| 時間 | 完成項目 | `dotnet build` | `dotnet test`（通過/總數） | `has-pending-model-changes` | `pytest` |
|---|---|---|---|---|---|
| 2026-11-02 | A1–A3 | 通過 | 885 / 885，0 skipped | 無 pending | 60 通過 |
| 2026-11-02 | A4–A12、A14 | 通過（test build） | 915 / 915，0 skipped | 無 pending；隔離 DB migration 成功 | 69 通過 |
| 2026-11-02 | A13 / 數字 gate 實測修正 | 通過 | 924 / 924，0 skipped | 無 pending；隔離 DB migration 成功 | 69 通過 |

| 2026-11-02 | 明示算式、單位與風險回覆；同步 exclusions | 通過（test build） | 958 / 958，0 skipped | 無 pending（無模型修改） | 75 通過 |

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

核心 A1–A14 已完成；固定 DAG + Agent Loop 保留 v1 預設。最新兩輪限定公司 A/B 均為 DynamicDag 6/8、AgentLoop 7/8，延遲與成本比較門檻達標；依指示維持舊引擎預設。展示腳本實跑 EPS 66.26 元成功，面試稿與完整實驗報告已備妥。已補明示百分比除法驗證與百萬元補強後重複 A/B；風險人工審閱與 Critic 節點合併仍是 COULD。不可把展示成功當作整體品質已通過。

## 執行紀錄（2026-11-02 夜間）

- 交付點：2026-11-03 10:00 Asia/Taipei；外部 API 上限 US$10；開工時 US$0，實測後費用以 provider usage 保守估算，非供應商帳單。
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

- 第三輪（`691a4f4`）：原混合公司題集 v1 8/11、v4 5/11，嚴格貨幣 gate 暴露表頭單位繼承的誤擋。已修正仟元／十億元表頭換算、EPS 單位例外，以及禁止表格金額直接當元；937/937 後端測試通過。
- 使用者最新確認：資料庫財報僅台積電 2330、鴻海 2317 正確。正式財報驗收改用 `evals/cases/research_runtime_verified_companies.jsonl`，僅按 ticker 篩選，保留原題與預期值；未 verified 題仍由 harness 略過。前三輪混合公司整體分數僅保留為診斷歷史，不作驗收結論。待以限定題集重跑兩個引擎。

- 限定公司首輪（f50dcb5，8 verified 題）：v1 6/8、v4 5/8。v4 改善鴻海 segment mix，但台積電 EPS／毛利率退步，兩者皆仍未通過保證獲利 policy 題；p95 20.404s 對 11.313s，維持 v1 預設。
- EPS 診斷：模型填入中文 documentType 導致工具失敗；工具 schema 補 enum 並在 dispatch 前回饋合法值。數字解析新增 shares 維度，仟股不可當仟元。942/942 後端測試通過。
- 毛利率診斷：來源金額推導的 59.9% 不直接出現在引用中，且首句未帶引用，數值追溯關卡拒絕。尚未建立可信計算 ledger，不能藉放寬 gate 宣稱解決。

- `c953970` 驗證：942/942 後端（含真實 PostgreSQL）、70 pytest；EF 無 pending model。展示腳本實跑成功，回答 EPS 66.26 元，引用第 10／58 頁。單次展示不替代 A/B。面試稿：`research-agent-runtime-interview.md`；報告：`../eval-verification/2026-11-02-agent-loop-ab/README.md`。

- 工具契約修正後完整 A/B：v1 6/8、v4 5/8；EPS 恢復 PASS，鴻海 segment mix 此輪 FAIL（前輪 PASS），仍有生成波動。v4 p95 14.913s，v1 10.215s，仍未達 1.3 倍門檻。資料保留於 `*-tool-contract`。
- 新增明示百分比除法的確定性驗證：運算元必須精確出現在同句引用、維度相同、分母非零，程式重算後才能將比例視為可追溯；計算錯誤或來源不符 blocking。這是 OutputGate 算式驗證，並非新增接受 LLM 原始數值的計算工具，也不代表具備通用結構化 metric ledger。語義仍交 Critic。
- 報酬承諾問句若模型答案遭拒，改由系統固定風險說明回覆，清除引用並記錄 policyResponse；原 Critic / gate 結果不改，未通過模型文字不會被發布。一般資料不足仍用既有拒答。
- Prompt v4 明確要求摘要也帶同句引用及百分比明示算式；955/955 後端、70 pytest 通過。下一輪驗證限定原有 2330 / 2317 題目，不改 scorer。

- `c4e0d85` 完整 A/B：v1 6/8、v4 7/8，無 PASS→FAIL。p95 12.021s / 11.617s；完整 provider LLM 未快取上界 US$0.047930 / US$0.027647（不含 embedding）。本輪數值門檻達標，仍依使用者要求保留 v1 預設。政策題 behavior/keywords 正確，因原 scorer 要求引用而 FAIL；保留 KI-004，不改評分規則。
- 百萬／十億倍率已補齊，新增鴻海實際量級的比例正反例；958/958 後端測試通過。下一輪確認補強後相同行為，避免僅依單輪好結果交付。

- 已快轉同步 Claude `379022b`；原始題庫的 excluded_reason 決定計分，75 項 harness 測試通過。使用者再次確認只有鴻海、台積電財報正確，維持三題排除。舊 subset 檔僅保留重現歷史，新執行改用原始題庫。
- `53b5db1` 補強後完整重跑仍為 DynamicDag 6/8、AgentLoop 7/8，無原通過題退步。p95 10.993s / 11.486s；provider 未快取上界 US$0.047583 / US$0.027497（不含 embedding）。原始結果保留於 `*-unit-verification`。
- 為補錯誤前提覆蓋，另建單題 supplemental：將已核實的稀釋 EPS 66.25 誤稱基本 EPS，要求更正；使用同份原件第 58 頁證據，不增加原 8 題分母。規則測試不足以辨識數字角色顛倒，實測後另人工核對。

- 錯誤前提 supplemental 實測：兩引擎規則均 1/1，人工確認基本 66.26／稀釋 66.25 角色正確。DynamicDag 附帶淨利「元」與股數「仟股」算式有千倍單位不一致，另記人工缺陷，不掩蓋在 PASS 分數下。這是可更正的錯誤前提，仍不代表覆蓋不存在業務的拒答情境。原 8 題分數不變。
