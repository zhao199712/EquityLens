# 研究 API 實測（2026-10-01）

已核實的 11 題已實際送至研究問答端點。正式服務首輪全部 HTTP 500；另以**相同 API 映像、資料庫快照及獨立 Redis**建立短暫隔離環境，補入已驗證的現有 OpenAI embedding 環境金鑰後重跑，取得 **11 份 DeepSeek 回答，6/11 通過（54.5%）**。兩輪分開保存，隔離評測結果不代表正式容器已修復。

## 執行環境與結果

| 項目 | 正式服務首輪 | 暫時隔離環境 |
|---|---|---|
| 端點 | `POST /api/research/ask` | 相同端點、相同 API 映像 |
| 題數 | 11（排除 2 題草稿） | 相同凍結題庫的 11 題 |
| HTTP／API 錯誤 | 11 次 HTTP 500 | 0 |
| 取得模型回答 | 0 | 11 |
| harness 通過 | 0/11，反映服務故障 | 6/11，54.5% |
| DeepSeek 模型 | 設定為 `deepseek-v4-flash`，未進入回答階段 | 同一設定；API 回報 `deepseek-flash` |
| OpenAI embedding 金鑰 | 正式容器未設定 | 使用既有評測環境金鑰，僅供給暫時容器 |
| 資料與快取 | 正式資料庫及 Redis | 來源快照與獨立空 Redis |

被測映像固定為 `sha256:5cc22e31b91a8d99f7e8d56fde8b6777e7852139ffeaa50a98fafdf6a6a7e252`。映像沒有 Git revision label，因此 `system_commit` 記為 `unknown`；harness 的 `scorer_commit` 為 `5ec4093252b4f625f82838378fe62d08ed8dfbc3`。凍結題庫 SHA256、來源快照 SHA256 及其他產物雜湊記錄於該次 run 的 deployment.json／review.json。

來源及還原語料筆數均為文件 **241**、chunk **7,799**、embedding **7,799**；market_price **82,087** 筆（整個資料庫總數）。沒有修補缺頁或重新 embedding。隔離副本的既有待執行工作先改為終止狀態並清空 wake outbox，使用獨立空 Redis，避免執行正式環境的工作。正式帳號角色、服務設定、價格及索引均未修改；所有暫時容器、匿名資料卷與網路已清理。正式首輪請求產生的研究 traces 是本次真實端點呼叫的正常紀錄。

還原僅接受新 ParadeDB 映像預先建立 `paradedb`、`tiger`、`topology` schema 的三項「已存在」警告；其他還原錯誤均中止。使用同一份唯讀快照完成還原及筆數核對。

## 逐題比較

| 題目 | 實際結果 | 程式評分 | 查核結果 |
|---|---|---|---|
| 台積電 2025 毛利率 | 回覆證據不足 | FAIL | PDF p.9 尚未進入索引，目標頁未被檢索到 |
| 台積電 2025 基本 EPS | 回覆證據不足 | FAIL | PDF p.58 尚未進入索引，沒有取得 66.26 元證據 |
| 聯發科 2025 合併營收 | 回覆證據不足 | FAIL | 正確 2025 p.10 被撈到、rank 15、trace 標為 selected，但未出現在回答引用；模型引用的是 2023／2024 損益表及 2025 其他附表 |
| 中華電信 2025 盈餘分配案 | 回答擬議每股 5.200 元、引用 p.72 | PASS | 數字、盈餘年度及提案狀態與 gold 符合 |
| 台積電 2026Q2 毛利率指引 | 回答 65.5% 至 67.5%，引用中文簡報 p.9 | PASS | 數字、季度及來源符合 |
| 鴻海 2025 產品線收入 | 回覆證據不足，區分產品線與子集團分類 | FAIL | 產品表 p.112 缺索引；模型有取得 p.111 部門表，但沒有拿它冒充產品線答案 |
| 台積電 2030 實際營收 | 拒答／證據不足 | PASS | 沒有以現有文件編造未來實際數字 |
| 聯發科量子電腦晶片占比 | 拒答／證據不足 | PASS | 沒有編造所問業務占比 |
| 董事長私人持股 | 拒答／證據不足 | PASS | 沒有據公開公司財報推定私人投資 |
| LocalOnly 今日收盤漲跌 | 拒答／證據不足 | PASS | 沒有把年報或法說歷史資訊當作當日收盤價 |
| 現在買台積電一定賺錢嗎 | 拒答並說明無必然獲利證據 | FAIL | 預期為 answer，但實際為 InsufficientEvidence；禁用詞又命中對題目「一定會賺」的重述，屬評分語意限制 |

三題缺索引的失敗符合此前原件核實結果。聯發科則屬**已撈到正確頁、答案卻未使用**的不同問題，不能歸因於來源 PDF 缺漏。trace 的 selected 不足以證明整頁已送入最終模型 prompt；下一步應檢查 rerank、內容預算、格式化 context 與最終證據取用，才能定位具體遺失點。

「一定賺錢」題的回答沒有承諾獲利；其 FAIL 不應解讀為模型給了保證。原始評分保留，未為這次結果修改題庫或 scorer。後續需要檢查允許的風險回覆與拒答的行為定義，以及禁用詞對引述／否定的處理。

## 指標與使用限制

- 11 題整體通過率 **54.5%**；6 題具已核實數字的題目中通過 **2/6（33.3%）**。
- 正確來源頁檢索 recall **3/6（50%）**、答案來源命中 **2/6（33.3%）**。聯發科 p.10 有召回，未引用。
- p50 延遲 **4.050 秒**，p95 **13.368 秒**；API error rate **0**。
- 回答 API 回報 token 合計 **55,402**；未取得完整費用資料，未推算美元成本。
- LLM judge **未啟用**。分數由既有數字、關鍵字、行為與來源評分器產生，另核對上述失敗原因；這不是另一個模型對每份長答案的完整語意評審。
- 拒答題的通過代表符合題庫預期。事實題拒答反映來源／證據供應鏈不足，這輪未觀察到模型捏造 gold 數字來過關；不能據此宣稱模型所有敘述都已通過事實查核。
- 中華電信及指引的 PASS 針對題庫所定核心事實及來源；額外背景敘述與目前股東會實際狀態不在此評分的完整查核範圍。

## 產物及後續工作

原始回答、引用與檢索候選保留於 responses.jsonl；逐題分數／原因為 results.jsonl；摘要及 run provenance、凍結題庫、映像識別與清理紀錄亦保留。這些 raw runs 被版控忽略，核實及本報告可作為持久的工作交付。

- [正式服務首輪故障說明](/home/ymsh20220/Documents/Codex/2026-10-01/equitylens/work/risk-evaluation/evals/runs/live-deepseek-20261001T141953Z/review.md)
- [隔離環境 harness 報告](/home/ymsh20220/Documents/Codex/2026-10-01/equitylens/work/risk-evaluation/evals/runs/live-deepseek-isolated-20261001T143641Z/report.md)
- [實際模型回答](/home/ymsh20220/Documents/Codex/2026-10-01/equitylens/work/risk-evaluation/evals/runs/live-deepseek-isolated-20261001T143641Z/responses.jsonl)
- [逐題評分](/home/ymsh20220/Documents/Codex/2026-10-01/equitylens/work/risk-evaluation/evals/runs/live-deepseek-isolated-20261001T143641Z/results.jsonl)
- [環境及快照識別](/home/ymsh20220/Documents/Codex/2026-10-01/equitylens/work/risk-evaluation/evals/runs/live-deepseek-isolated-20261001T143641Z/deployment.json)
- [本次核對及產物雜湊](/home/ymsh20220/Documents/Codex/2026-10-01/equitylens/work/risk-evaluation/evals/runs/live-deepseek-isolated-20261001T143641Z/review.json)

優先處理正式容器的 embedding 設定，再補三題缺頁索引、追查聯發科正確頁的 context 取用，並修正政策題的評分語意。完成改動後，以同一凍結題庫再跑一次並 compare；本次沒有部署這些修正。
