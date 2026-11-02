# Research Agent Runtime v4 — 實作進度

> 對應設計：[`research-agent-runtime.md`](research-agent-runtime.md)。驗收以設計文件開頭的「任務簡報」A1–A17 為準。
> 實作者每完成一個項目就更新本檔並 commit。Reviewer 早上只看這一份就能知道做到哪裡。

> 最新狀態（2026-11-03 00:02）：使用者已授權正式部署，AgentLoop 已成為正式預設，四項 migration 已套用且 smoke 通過。下方保留舊預設的描述為當時歷史決策，已由此次授權取代。詳見 [部署紀錄](research-agent-runtime-deployment.md)。

## 開工基準

### 2026-11-03 移除靜態頁面

後續正式站重整已確認導覽生效。Ask Agent 的台積電 EPS 提問與「那 2024 年呢」追問皆建立 ResearchAgent 並 Succeeded，結果回填與對話保存正常；Agent Runs 列表、ID 搜尋、詳情及除錯事件正常。Workflow 篩選缺少 ResearchAgent、重整先開空白對話仍待改善。詳見 [UI 驗收紀錄](../eval-verification/2026-11-03-ui-smoke/README.md)。

依使用者要求刪除 Dashboard、Financial Reports 列表與明細元件及導覽。一般登入／註冊與首頁 CTA 改到 Portfolios；舊網址分別重新導向 Portfolios / Research。移除已刪頁面的測試，更新登入與路由測試；前端 build 成功、17 個測試檔案共 110 項通過。Research、風險頁與管理端報表管理保留。

### 2026-11-03 01:05 正式 UI 驗收

台積電 Reports 財報管理另行實測未通過：列表建立僅存前端記憶體，返回即消失；明細、引用與 Critic 均為硬編碼，營收 KPI 與內文不一致。此頁尚未串接後端，與已通過的 Research AgentLoop 分開看待。證據見同份瀏覽器驗收紀錄。

後續依使用者指定，以台積電、聯發科、台達電、富邦金、鴻海另建五檔組合重跑：risk 與 monte-carlo 均 Completed，採 VT-GARCH-t + Joint-Vector FHS，無 fallback，路徑圖已顯示。1 日 VaR 95% -2.51%、ES 95% -3.51%；台積電權重 81.77%。模型統計驗證仍 Unvalidated，詳細參數與證據見下方驗收紀錄。

獨立帳號研究 smoke 通過（ResearchAgent 8.4 秒、EPS 66.26、兩項引用）；0050 全部 50 檔模擬持倉建立完成。Risk Run 建立遭資料門檻拒絕：6446 只有 241 筆，低於實際 352 共同日要求；沒有產生風險結果。另發現新增持股競態、門檻錯誤文案及無 run 仍顯示 QUEUED。詳見 [瀏覽器驗收紀錄](../eval-verification/2026-11-03-ui-smoke/README.md)，此三項尚待修補。

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

- 2026-11-02 23:46 續跑：Git clean，必做／應做已完成，保留 COULD 項。新增展示腳本離線 replay，顯示來源 commit／時間、原始評分、回答、引用及工具統計；不偽造完整 turn trace、不呼叫 API。成功 EPS、政策 FAIL、錯誤前提與不存在 case 的錯誤處理皆實跑確認。此次外部 API 費用 US$0。

## 合併 main 與舊版備份

- 使用者明確授權先備份舊版，再合併新架構至 main。
- 合併前 main：`434e645d466d0a5104b54dbaffcc279f95bde583`；遠端備份分支 `backup/main-before-research-runtime-20261102-434e645` 已推送並核對相同 SHA。此為 Git 程式碼快照，非資料庫備份；正式 DB 未變更。
- 合併來源：`feat/research-agent-runtime`，tip `c395b9b`；包含 Claude excluded_reason `379022b`。保留 merge commit 與功能分支歷史，不 squash。
- 合併前 Release build/test：958/958（含隔離 PostgreSQL、零 skipped）；eval harness 75/75；題庫 validate：13 題、2 未核實、3 excluded。
- 預設仍為 DynamicDag，新引擎以 engine=AgentLoop 啟用。沒有部署或操作正式服務；GitHub workflow 僅 build/test。四項新 migration 只在隔離 DB 驗證，實際部署前仍需套用。

- 2026-11-03 正式部署：先保留舊映像與完整 DB 備份（含停止 API 後快照），再套用四項 migration、重啟 API。公開 HTTPS 請求省略 engine，確認 ResearchAgent / Succeeded / Answered，EPS 66.26，引用第 10／58 頁。正式 compose 啟用 AgentLoop 與 thinking=false；其餘既有設定保留。續跑指令已更新，避免回復過期的舊預設要求。

## AgentLoop 前端呈現更新（2026-11-03）

- ResearchAgent 詳情新增五階段摘要、最終答案／引用、獨立品質結果與 LLM 呼叫紀錄；不再用 16 個實體節點百分比代表研究進度。修訂略過標示不需要修訂。
- 接上既有 llmTurns；統計包含已記錄 attempts，費用僅代表研究／修訂迴圈，不含其他模型與檢索服務。證據數明確標為最後一輪。
- Ask Agent 卡片移除 ResearchAgent 節點進度；既有卡片 API 沒有時間欄位，因此耗時放在詳細頁，不新增輪詢或後端 API。其他 workflows 與批准、取消、重試維持既有路徑。
- 前端 18 個測試檔、120 個測試通過，正式 build 通過；新增 10 個測試涵蓋暫存答案隔離、拒答、政策回覆、修訂略過、重試統計及舊資料缺漏。瀏覽器發布驗證另記 UI smoke 報告。
- 正式前端 `46d697d` 已發布；121 個前端測試及 GitHub 各項 CI 通過。瀏覽器驗證成功紀錄、拒答紀錄、追問、ResearchAgent 篩選及 390px 手機版。新基本 EPS 題因數值追溯被 gate 拒答；稀釋 EPS 追問成功 66.25。保留答案品質缺口，未放寬 gate；完整證據見 UI smoke 報告。
- 01:42 使用者指定國巨高權重測試：在原五檔模擬投組新增 2327 共 3,407 股，權重 50%。Ask Agent 兩次 PortfolioDiagnosis 成功發布；舊風險快照未重用。發現修正追問路由不支援 PortfolioDiagnosis，以及缺少假設權重重算；初次敘述有風險貢獻／厚尾過度推論，第二份明確承認限制並修正解讀。詳見 UI smoke 國巨測試節，未進行程式修復或變更後端。

## 投組四項修正（2026-11-03 02:06）

- 功能分支 `feat/portfolio-scenario-analysis`：PortfolioDiagnosis 修正追問保留父版本／投組／期間；逐股風險貢獻；唯讀減碼轉現金工具；期間／品質／情境比較 UI。預設分析一年，短樣本僅提供配置。
- 後端 977/977（含真實隔離 PostgreSQL，零 skipped）、前端 124/124、production build 通過。獨立 Python 對保存的合成價格快照重算原組合與兩情境，四項風險指標誤差 <1e-8。
- 瀏覽器隔離驗收：國巨 50% → 30%／20% 兩情境、報告批准成功；原對話修正追問建立子診斷成功，六檔持股／零筆交易不變。首次發現舊診斷與情境基準容易混用，已補提示與畫面標示，第二輪明確使用同口徑基準。
- 實測是明確標示的合成行情，非正式價格回測。原財報題庫及分數不動。六次模型呼叫按既有設定估算 US$0.009039，非帳單；正式環境未部署、未變更。
- 報告、原始輸出、可重跑驗證與截圖：`../eval-verification/2026-11-03-portfolio-scenarios/README.md`。


## 02:08 排程驗收與面試文件核對

- 起始 Git clean，功能分支已與 origin 同步（`d2a0b71`）。沒有新增功能或再次部署。
- 修正面試稿及 A/B 報告開頭過期的 v1 預設描述；明確區分歷史實驗決策、正式 AgentLoop 預設，以及尚未部署的投組情境功能。
- 面試稿新增合成行情情境展示、LLM／確定性計算分工、風險貢獻口徑、追問父子關係與尚未完成的真實行情／模型回測。
- 離線 EPS replay 成功：原 `53b5db1` 紀錄回答 66.26 元，來源第 10 頁；投組獨立快照重算三列 PASS。本次無外部 API 呼叫，新增費用 US$0。


## 02:12 使用者授權投組部署

- 使用者明確要求部署及推送 GitHub；main merge `d8af5ef` 已推送，前後端發布成功。API healthy，AgentLoop 預設與原環境設定不變。
- 部署前保留 Git 分支、舊 API 映像與私有 DB dump；本次無 migration、無持倉／交易變更。正式瀏覽器登入及歷史報告可讀，已看到新版 UI；沒有新模型呼叫。
- 面試稿與情境報告同步更新部署狀態，保留合成行情驗收與真實行情回測的區分。詳細備份與映像見部署紀錄。

## 03:09 部署後 CI 核對

- 起始 main `8e8e0c6` 工作目錄乾淨、與 origin 同步。使用者要求的 README 設計決策移除已保留，不重新加入架構解說。
- 該提交的 Cloudflare Pages、Backend、Frontend、Mathematics、Eval harness 五項遠端檢查皆 success。正式 API healthy，預設仍為 AgentLoop，映像與 02:12 部署一致。
- 可核對的提交 SHA、檢查連結及時間保存在 `../eval-verification/2026-11-03-portfolio-scenarios/release-checks.json`。本次只補部署後驗證紀錄，沒有新增功能、部署、模型呼叫或資料變更，新增費用 US$0。
- 仍未宣稱完成：正式行情下的新情境完整問答回歸、VaR 覆蓋率回測、獨立 holdout 題組；現有合成行情與開發集成績不替代這些驗收。

## 04:10 故障恢復展示驗收

- 起始 main `a95831a` 工作目錄乾淨。依面試稿執行兩項故障恢復測試，2/2 通過，無略過：`CrashWithinTurn_ReusesPersistedSuccessfulCall`、`CrashAfterExternalEffect_ReplaysSameIdempotencyKey_NotTheEffect`。
- 面試稿補上可解說的斷言：已保存工具不新增呼叫；副作用恢復呼叫兩次但效果一次。明示前者使用預建中斷狀態，後者以取消注入故障，均不是實際 kill process／DB crash 演練。
- 將記憶體 fake 工具驗證與既有真實 PostgreSQL 整合驗證分開描述，避免面試過度承諾 exactly-once。
- 本次只更新 docs，不改 README、產品程式或正式服務。外部 API 新增費用 US$0。
