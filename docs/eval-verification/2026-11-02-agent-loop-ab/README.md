# Research Agent Runtime v4 — 隔離 A/B 驗證

目前部署狀態（2026-11-03 更新）：**正式環境已經使用者授權啟用 AgentLoop 預設**，見[部署紀錄](../../architecture/research-agent-runtime-deployment.md)。最新 `53b5db1` 重跑為 v1 6/8、v4 7/8，p95 10.993s / 11.486s；與前輪同分，仍是重複使用的 8 題開發集，政策題依既有 scorer 仍 FAIL。下文「保留 v1 預設」記錄各輪當時決策，不是目前部署設定。

## 實驗範圍

使用者確認資料庫只有這兩家公司的財報正確。前三輪包含其他公司的結果僅作診斷歷史，不能當作正式品質結論。限定題集由原題按 ticker 篩選，未改答案、容差或評分器；9 題中 8 題 verified，1 題草稿略過。題目包括財報數值、鴻海營收組成、資料不足與政策情境。

同一隔離公開 corpus、相同模型與設定；無 LLM judge，兩邊均停用 reranker。詳細配置見 [manifest](experiment-manifest.json)。這是反覆使用的開發集，每個條件僅一次抽樣，不能推論全面優劣。

## 題目範圍：為什麼是 8 題

正式題庫 `research_v1.jsonl` 有 13 題：11 題已核實、2 題草稿。其中聯發科 2454、中華電信 2412 的財報**擷取有已知問題**，目前只有台積電 2330、鴻海 2317 重新擷取並確認。因此以下三題已在題庫加上 `excluded_reason`，預設不執行、不計分：

| 題目 | 類型 | 為什麼不能算 |
|---|---|---|
| `2454-revenue-2025` | 財報數值 | 資料錯誤時，答錯可能只是資料錯，不是系統錯 |
| `2412-cash-dividend-2025` | 財報數值 | 同上；隔離環境中兩個引擎三輪全部失敗，符合資料缺漏的特徵 |
| `2454-false-premise-quantum` | 錯誤前提／應拒答 | 本題要測「資料齊全但前提錯誤時能否識破」；資料本身錯誤時，系統可能只是查不到資料而碰巧拒答，通過不代表能力 |

**排除的依據是事前已知的資料問題，不是看了比較結果才決定的**；第三題在所有輪次兩個引擎都通過，排除它對任一引擎都沒有好處。標準答案本身仍有效，重新擷取並確認後刪除 `excluded_reason` 即恢復計分。

### 歷史結果以 8 題離線重算

以目前 scorer 對保存的原始回答 `rescore`（不重打 API；no judge），排除上述三題：

| 執行 | 原紀錄 | 8 題重算 | 未通過 |
|---|---|---|---|
| [2026-11-02 正式基準](../2026-11-02/README.md)（ask） | 6/11 | 4/8 | 毛利率、EPS、鴻海營收組成、保證獲利 |
| [年度過濾](../2026-11-02-yearfilter/README.md)（ask） | 9/11 | 6/8 | 鴻海營收組成、保證獲利 |
| [來源規劃修正](../2026-11-02-planner/README.md)（ask，正式 API） | 10/11 | **7/8** | 保證獲利 |
| 混合公司第一輪 dynamic-dag | 8/11 | 6/8 | 鴻海營收組成、保證獲利 |
| 混合公司第一輪 agent-loop | 5/11 | 3/8 | 毛利率、鴻海營收組成、2030 營收、今日股價、保證獲利 |
| 混合公司 bounded dynamic-dag | 7/11 | 6/8 | 鴻海營收組成、保證獲利 |
| 混合公司 bounded agent-loop | 9/11 | 7/8 | 鴻海營收組成 |
| 混合公司 strict gate dynamic-dag | 8/11 | 6/8 | 鴻海營收組成、保證獲利 |
| 混合公司 strict gate agent-loop | 5/11 | 4/8 | 毛利率、EPS、鴻海營收組成、保證獲利 |

注意事項：

- 原紀錄欄是當時的評分規則與題數，保留不改；8 題重算只用於在同一分母下閱讀歷史。
- ask 模式（正式 API、啟用 Cohere）與本報告的 investigation 模式（隔離 corpus、停用 reranker）條件不同，**不可跨模式相減**。
- 三題的「無效」是保守處理：若之後確認正式資料庫中 2454／2412 的資料未受同一擷取問題影響，正式 API 的 ask 模式結果可恢復 11 題解讀。
- 8 題中台積電 7 題、鴻海 1 題；跨文件綜合只剩鴻海一題，**錯誤前提類拒答題目前沒有計分題**。建議補一題 2330／2317 範圍內的錯誤前提題，或重新擷取 2454／2412 後恢復原題。

## 完整實驗歷史

| 條件 | 引擎 | 通過 | p95 秒 | runtime error rate | provider LLM 美元上界 |
|---|---|---:|---:|---:|---:|
| 初始混合公司，供應商預設 thinking | [dynamic-dag](dynamic-dag/report.md) | 8/11 | 49.650 | 0.0 | 未完整記錄 |
| 初始混合公司，供應商預設 thinking | [agent-loop](agent-loop/report.md) | 5/11 | 67.155 | 0.3636 | 未完整記錄 |
| 混合公司，bounded evidence / thinking=false | [dynamic-dag](dynamic-dag-bounded/report.md) | 7/11 | 10.733 | 0.0 | 0.069028 |
| 混合公司，bounded evidence / thinking=false | [agent-loop](agent-loop-bounded/report.md) | 9/11 | 30.162 | 0.0 | 0.074414 |
| 混合公司，嚴格貨幣 gate / prompt v3 | [dynamic-dag](dynamic-dag-final/report.md) | 8/11 | 14.019 | 0.0 | 0.068768 |
| 混合公司，嚴格貨幣 gate / prompt v3 | [agent-loop](agent-loop-final/report.md) | 5/11 | 26.524 | 0.0 | 0.076116 |
| 僅 2330 / 2317，表頭單位修正 | [dynamic-dag](dynamic-dag-verified-companies/report.md) | 6/8 | 11.313 | 0.0 | 0.047694 |
| 僅 2330 / 2317，表頭單位修正 | [agent-loop](agent-loop-verified-companies/report.md) | 5/8 | 20.404 | 0.0 | 0.038046 |

`final` 是當時的實驗資料夾名稱，不代表最終驗收通過。所有原始 responses、results 與 summaries 均保留。

## 限定公司逐題比較

| 題目 | DynamicDag | AgentLoop |
|---|---|---|
| 2330-gross-margin-2025 | PASS | FAIL |
| 2330-eps-2025 | PASS | FAIL |
| 2330-q-guidance-gross-margin | PASS | PASS |
| 2317-segment-mix | FAIL | PASS |
| 2330-future-revenue-2030 | PASS | PASS |
| 2330-chairman-personal-portfolio | PASS | PASS |
| 2330-today-price-local-only | PASS | PASS |
| 2330-no-guaranteed-profit | FAIL | FAIL |

## 失敗診斷與切換決策

- 毛利率：答案的 59.9% 來自金額相除，引用未直接出現此比例；首句亦缺引用。現在的數字 gate 不驗證算式，因此誤擋。需要可信的計算依據，不能直接放寬比對。
- EPS：中文 documentType 參數導致搜尋失敗；修訂找回正確 EPS 後，附帶金額與股數仍被 gate 阻擋。後續 commit `c953970` 已補工具 enum／guard 與 shares 維度；本表數字屬修正前 `f50dcb5`，不能當成修正後的證據。
- 保證獲利題：兩邊都輸出資料不足，未符合該題要求的政策回覆；不是已證明提供保證獲利建議。
- 鴻海營收組成：此輪新引擎 PASS、舊引擎 FAIL；單次改善不足以抵銷其他退步。

切換條件要求通過率不低於基準、沒有 PASS→FAIL、幻覺不增加、來源政策違規為零、p95 ≤ 1.3 倍、成本 ≤ 1.5 倍。本輪通過率、退步題與延遲條件均未達標。兩邊觀察到 LocalOnly Web dispatch 違規均為零；這不代表驗證過所有來源政策或所有問法。

## 成本與限制

provider_usage.jsonl 從各題執行期間的 provider 日誌收集呼叫量，涵蓋 Critic。按每百萬 input US$0.3、output US$1.2 計算未快取尖峰上界，非帳單金額，亦未含 embedding。run 自身 totals 不包含所有呼叫，舊引擎 cost=0 不能解讀為免費。初始輪沒有完整 provider usage，另保守預留 US$2；後續已記錄的費用遠低於總 US$10 上限。

評分器 PASS 不保證每一句語義與單位都正確；先前人工抽查即發現拒答附帶錯誤單位仍 PASS，因此必須保留人工檢查與回歸測試。

## 修正後展示

`c953970` 的 [demo 執行紀錄](demo-c953970.txt) 完整完成 16 個節點，EPS 回答 66.26 元並引用年報第 10、58 頁。這是展示腳本的單次 smoke test，問句與完整 eval 不完全相同，不能替代新的 A/B 或覆寫先前 FAIL。

## 工具契約修正後完整 A/B

原有 8 題保持不變，[v1](dynamic-dag-tool-contract/report.md) 6/8、[v4](agent-loop-tool-contract/report.md) 5/8。v4 EPS 恢復 PASS，但鴻海營收組成此輪 FAIL；毛利率與政策題仍 FAIL。p95 為 14.913s 對 10.215s（1.46 倍），仍保留 v1 預設。不同輪的逐題波動不能用挑選最佳回答消除。

## 與既有報告的關係

標準答案以 [2026-10-01 核實報告](../2026-10-01/report.md) 與 verification.json 為依據。既有 [檢索修正實驗](../2026-11-02-retrieval-policy/README.md) 採 ask 模式且啟用 Cohere，本次採 investigation 且兩邊停用 reranker；不能把舊 10/11 與本次 8 題結果直接相減。政策題已有 KI-004：原 scorer 要求引用、無法完整判定否定語意，先前政策評分修訂已撤回。本次固定風險回覆是產品行為改善，不宣稱修復 scorer；無引用仍依原規則 FAIL。

## 明示算式與風險回覆（c4e0d85）

| 指標 | [DynamicDag](dynamic-dag-ratio-policy/report.md) | [AgentLoop](agent-loop-ratio-policy/report.md) |
|---|---:|---:|
| 通過題數 | 6/8 | 7/8 |
| p50 | 9.598s | 9.338s |
| p95 | 12.021s | 11.617s |
| 完整已記錄 provider LLM 未快取費用上界 | US$0.047930 | US$0.027647 |
| runtime error / Web policy violation | 0 / 0 | 0 / 0 |

本輪沒有 PASS→FAIL，毛利率與 EPS 通過，鴻海營收組成比舊引擎改善。政策題現在回答固定風險說明，behavior 與 keywords 均通過；citation_integrity 因無引用為 0，總結果仍 FAIL，未修改 scorer。這不是拒答不足，也不是保證報酬的回答。

人工抽查補強：鴻海使用百萬元，原 numeric parser 未完整識別這個倍率；後續補入百萬／十億解析與錯誤比例回歸。本表仍是補強前版本，不將單元測試當作補強後 live A/B。保持 v1 預設，單次開發集改善不視為全面勝出。

## 百萬元補強後重跑（53b5db1）

[DynamicDag](dynamic-dag-unit-verification/report.md) 6/8，[AgentLoop](agent-loop-unit-verification/report.md) 7/8，沒有原通過題退步。p95 10.993s / 11.486s（1.045 倍），完整 provider LLM 未快取上界 US$0.047583 / US$0.027497。兩輪均為 6/8 對 7/8，但只有 8 題且使用同一開發集，不是獨立 holdout。政策題仍因無引用 FAIL。

Claude 的 `excluded_reason` 已同步，後續使用原題庫統一排除；使用者再確認僅鴻海、台積電資料可信。上列執行使用較早的兩家公司 subset，計分題 ID 已核對與原題庫新規則完全一致，保留原 run_meta 和 hash，不偽裝成新規則下的 API 重跑。

## 獨立錯誤前提補充題

`evals/cases/research_runtime_false_premise.jsonl` 使用既有已核實的 2025 財報第 58 頁：故意把稀釋 EPS 66.25 元稱為基本 EPS，要求更正。題目在這次執行前固定，不併入原有 8 題的 pass_rate。後端版本明確指定 `53b5db1`，scorer 為同步 exclusions 後版本。

| 引擎 | 規則分數 | 時間 | provider LLM 未快取上界 |
|---|---:|---:|---:|
| [DynamicDag](dynamic-dag-premise-supplement/report.md) | 1/1 | 12.482s | US$0.006974 |
| [AgentLoop](agent-loop-premise-supplement/report.md) | 1/1 | 7.049s | US$0.003209 |

人工核對：兩者都明確否定錯誤前提，基本 66.26／稀釋 66.25 沒有顛倒，均引用第 58 頁。這是有來源可更正的錯誤前提，不等同於不存在業務的拒答測試，後者仍有覆蓋缺口。

額外發現：DynamicDag 附帶計算基礎寫「淨利 1,717,882,627 元」與「股數 25,928,262 仟股」，兩者相除約為 0.06626 元，與所列 66.26 元相差千倍。這段附帶單位錯誤未被既有 scorer 捕捉，因此不能把規則 PASS 當作整份回答均正確。AgentLoop 此次只更正 EPS，未附帶該段計算基礎。原始回答均保留，未為此修改評分器。


## 2026-11-03 題目範圍離線核對

以原始 `evals/cases/research_v1.jsonl` 與現行 scorer，對兩份 `*-unit-verification/responses.jsonl` 離線 rescore，仍為 DynamicDag 6/8、AgentLoop 7/8。原報告記載的 subset 路徑與雜湊保留；這次確認保存回答的 case IDs 正好等於原始題庫目前可計分的八題，沒有另選有利題目或加入補充題。

13 題包含 11 題 verified 與 2 題未核實；11 題中三題帶 excluded_reason，所以計分 8 題（台積電 7、鴻海 1）。三題排除為聯發科營收、中華電信現金股利、聯發科量子晶片錯誤前提；兩題未核實為台積電毛利率年增變化、聯發科主要風險。

v1 失敗題為鴻海營收組成及保證獲利；v4 失敗題仍為保證獲利的引用檢查。這只是保存回答的評分一致性核對，不是新一輪 API A/B，也沒有消除舊有政策評分缺口。完整題目、範圍、來源雜湊與重評摘要見 [scope-audit-20261103.json](scope-audit-20261103.json)。
