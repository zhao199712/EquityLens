# Research Agent Runtime v4 — 隔離 A/B 驗證

目前結論：**保留 DynamicDag 預設，AgentLoop 僅 opt-in**。最新正式驗收限定台積電（2330）與鴻海（2317）；8 題中舊引擎通過 6 題、新引擎通過 5 題，且有原本通過題退步，延遲也未達門檻。

## 實驗範圍

使用者確認資料庫只有這兩家公司的財報正確。前三輪包含其他公司的結果僅作診斷歷史，不能當作正式品質結論。限定題集由原題按 ticker 篩選，未改答案、容差或評分器；9 題中 8 題 verified，1 題草稿略過。題目包括財報數值、鴻海營收組成、資料不足與政策情境。

同一隔離公開 corpus、相同模型與設定；無 LLM judge，兩邊均停用 reranker。詳細配置見 [manifest](experiment-manifest.json)。這是反覆使用的開發集，每個條件僅一次抽樣，不能推論全面優劣。

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
