# 財報到投組決策：多輪評估集 v1

這份評估集沿著「某家公司財報如何 → 對我的投組可能有什麼影響 → 如何降低波動」驗收連續任務，並以 journey-00 重現 2026-11-04 正式站回報的投組記憶問題。

**目前狀態：** 只有題庫、fixture 規格與 validator。尚未實作 runner、seed 與逐輪 scorer，也沒有執行任何模型評估。所有案例標記為 `draft`，不產生通過率。

- 題庫：`evals/cases/conversation_journey_v1.jsonl`（11 組、45 輪）
- Fixture 規格：`evals/cases/conversation_journey_v1.fixture.json`
- 檢查字典與 validator：`evals/equitylens_eval/conversation.py`
- 既有研究 oracle：`evals/cases/research_v1.jsonl`
- 既有投組數學與隔離執行參考：[portfolio-eval.md](portfolio-eval.md)

```bash
cd evals
python -m equitylens_eval.conversation validate
python -m pytest tests/test_conversation.py
```

## 範圍與行為契約

保留既有研究與 14 題投組驗收的分母。這 11 組案例是新的獨立 suite，不能直接交給現有 research／portfolio CLI。

每個案例從空白 Session 開始，連續送出題庫中的原始訊息。`expected`、fixture 別名與成功條件只提供給評分器；不得把它們放進模型上下文、補 portfolioId，或替使用者改寫省略句。遇到非預期澄清時，不得臨時補提示救回案例；保存失敗與後續結果。每輪收集完成後才進入下一輪，不以 HTTP 200 或 `run_created` 當成完成。

公司財報問題預期 ResearchAgent；持倉影響與降波動問題預期 PortfolioDiagnosis。不要求在同一個 Run 混用兩類工具，也不要求特定的內部 context 欄位或固定工具順序。重點是使用者看得到的結論有證據，實際使用的資源身分正確。

「影響」輪至少需要：
1. 沿用前輪一項已核實、可追溯來源的財報事實。
2. 讀取選定投組的實際持倉與權重，指出直接曝險；ETF 穿透資料缺失時明示未知。
3. 解釋可能的影響途徑與限制。歷史波動不等於這份財報造成的變動；不要求捏造財報對股價或未來報酬的精確效果。

降波動輪需用同一快照比較基準與合法方案，核對權重、現金與 NAV 守恆並說明代價。開放問題不固定唯一權重或唯一工具序列；具體目標權重、現金或轉入持股則必須精確遵守。情境是假設分析，不改持倉。比較後建議維持也必須有證據；資料不足的案例以明確限制回答為成功。

## 失敗分類

每個失敗必須標記為下列其中一類，報告分開統計，不混成一個通過率：

| 類別 | 意義 |
| --- | --- |
| `bug` | 產品宣稱支援、應該要會的行為失敗。 |
| `capability_gap` | 產品尚未支援的能力。目前只有 `research_to_exposure`：研究 run 的結論還無法作為證據傳入投組 run。在產品支援並另有規格之前，這項失敗不計入回歸。 |
| `dependency_failed` | 前一輪（通常是研究）失敗，導致下游證據斷言無法成立。根因記在前一輪；後輪不得用標準答案補洞，整段不通過。 |

## Fixture 與資料就緒狀態

每個案例使用 owner_a 的兩個投組「組合甲／組合乙」，另設 owner_b 的同名投組作為隔離干擾。名稱不暗示哪個波動較高。

| Profile | 組合甲 | 組合乙 | 說明 |
| --- | --- | --- | --- |
| `standard` | 2330 60%、2317 25%、0050 15% | 2330 20%、2317 30%、0050 50% | 253 個價格點 |
| `no_direct` | 2317 60%、0050 40% | 2317 30%、0050 70% | 組合甲沒有 2330 直接持股；不提供 0050 成分權重 |
| `short_history` | 同 standard | 同 standard | 61 個價格點，少於風險分析所需的 121 個共同交易日 |

這些是待實作的合成行情規格，**不是已 seed 的資料或實際投組**。既有 `portfolio_seed` 每個帳號只有一個投組，不能直接拿來執行本題庫。同一個 ticker 在資料庫中只能有一筆 security，研究標的與持股要解析到同一筆；不同投組共用同一個價格序列。

**as_of 政策（`seed_date_frozen`）：** 產品的診斷期間預設以執行當天往前推一年，因此行情日期取 seed 當天的 UTC 日期，並寫入私有 manifest 凍結。同一天的不同版本可以共用 fixture；跨日期 live 重跑必須重新 seed，fixture hash 不同時，不得宣稱回歸改善。已保存的結果可以隨時離線重評。

數值 golden 尚未產生與核實，fixture 的 `numeric_golden` 為 `null`，validator 會拒絕預填的數值。獨立核實持倉權重、NAV、波動、VaR、ES、回撤與情境差異之後，案例才能從 `draft` 升級為 `verified`。

研究輪引用 `research_v1` 中已核實的 2330 毛利率、基本 EPS 與 2317 產品別收入題目；validator 會確認這些題目為 verified、未被排除，且 ticker 一致。研究 oracle 是 verified，不代表整段對話自動 verified。

## 欄位解讀

- `max_user_turns` 等於使用者輪數（包含重置訊息）；模型內部 loop、工具次數與費用另設預算。
- `intent` 決定該輪是否建立 run 與預期的工作流；不建立 run 的輪次（列出、選定、澄清、重置）不得帶 ticker、研究引用或情境限制。
- `portfolio_ref` 是 fixture 別名（`alpha`、`beta`），由私有 manifest 對應到實際 ID，只用於驗證。每輪檢查 Run input、工具有效 scope 與快照，不只看工具參數。
- `evidence_from_turns` 指定必須承接的前輪證據或任務，只能引用更早的輪次，而且不能跨過重置。只有 parentRunId 存在還不夠，也不強制一定採用 parent link。
- `research_case_refs` 重用事實與引用 oracle，不會把單輪研究題的 request 參數送進 Chat。來源政策若不能由真實入口設定或證明，不能宣稱測過 LocalOnly。
- `constraints` 是使用者指定的方案：`cash` 不得指定轉入標的；`holding` 必須轉入另一檔已持有的證券；目標權重必須低於目前權重。holding 模式的接收標的是 `preserve_other_holdings` 的例外。
- `conversation_success` 是整段對話的語義目標，需要人工覆核或經校準的 judge，不是可執行的斷言。沒有評分器的條件不能自動算通過。
- `review_status: draft` 表示 fixture 與完整 oracle 尚未驗證；不是模型已失敗，也不是已通過。

每項斷言保存 `pass`／`fail`／`unverifiable`／`not_applicable` 與證據位置，失敗時另存失敗分類。必要證據缺失不得算 pass。完整對話通過需要所有必備條件與適用的語義條件成立；身分錯誤、越權或副作用任何一項發生即失敗，不能被答案品質平均掉。

待審批的投組報告需保存 draft、Quality Gate 與審批後結果。未來的 runner 只能在隔離合成帳號的政策允許時核准唯讀 WorkflowGate，不能處理交易或 ToolCall 授權。

## 驗收檢查字典

`scorer` 欄決定由誰判定。**階段 1 只執行 deterministic 項目**，不需要 judge 或校準；semantic 項目在 judge 校準完成前一律標記為 unverifiable，不得算通過。

| ID | scorer | 驗收意義 |
| --- | --- | --- |
| `same_session` | deterministic | 每案例新 Session，所有輪次使用同一 Session，除重置語義外不重建。 |
| `owner_isolation` | deterministic | 以實際資源查詢與執行證據證明只用 owner_a 的資源；回答中的名稱一致不足以通過。 |
| `no_holdings_mutation` | deterministic | 案例前後持股、交易與現金沒有被分析或情境模擬改動。 |
| `trace_complete` | deterministic | 每輪有可歸因的原始輸出與必要執行證據；缺失標為 unverifiable。 |
| `resource_binding` | deterministic | Run input、工具有效 scope 與資料快照對應 portfolio_ref 的實際 ID。 |
| `no_premature_execution` | deterministic | 必要參數未明時提出具體澄清，不先建立猜測的 Run。 |
| `owned_portfolio_list` | deterministic | 只列出目前使用者的組合甲與組合乙，數量為 2，不列 owner_b 的同名投組。 |
| `selection_retained` | deterministic | 之後有需要的輪次可以解析到已選定的投組；不強制某種內部 context 欄位。 |
| `reset_boundary` | deterministic | 重置前的歷史即使留在 UI 顯示，也不得作為新輪次的指代、任務或證據。 |
| `computed_comparison` | deterministic | 基準與方案共用快照、期間與數學定義；獨立重算權重、NAV 與風險並檢查差異。未指定權重時不固定唯一答案；建議維持需有比較與理由。 |
| `research_grounded` | deterministic | 依 research_case_refs 的已核實事實與來源檢查財報答案；不把其 request 欄位偷偷注入 Chat。 |
| `no_unsupported_claims` | semantic | 不把財報當成未來報酬或波動的保證，不捏造數字與未取得的資料。 |
| `research_to_exposure` | semantic（capability_gap） | 前輪有來源的財報事實與本輪讀取的持倉形成可追蹤的影響說明，不能只檢查公司關鍵字。 |
| `impact_uncertainty` | semantic | 區分已知財報、直接與未知間接曝險、條件性推論與歷史風險；不把因果或預測當事實。 |
| `objective_retained` | semantic | 降低波動的目標在補參數或切換後沒有遺失。 |
| `tradeoffs` | semantic | 說明現金、集中度、曝險變動與改善限制，不要求未驗證的未來報酬數字。 |
| `limitations_disclosed` | semantic | 說明資料不足與影響，保留可支持的資訊，不偽造完整計算。 |

## 完整對話腳本

下列訊息與 JSONL 相同。不預寫 Agent 回答，避免把模型自己的回答當成標準答案。每輪的必要行為描述預期，不限制措辭。

### journey-00：列出投組 → 只回名稱選定 → 省略句追問

Fixture：`standard`；3 輪。重現 2026-11-04 正式站回報：使用者選定「高beta」後問「這個波動率如何」，卻被要求重新指定投組。不依賴研究輪，避免投組記憶的失敗被研究失敗蓋過。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 我有幾個投資組合？ | list_portfolios |
| t2 | 組合甲 | select_portfolio；組合甲 |
| t3 | 這個波動率如何？ | portfolio_diagnosis；組合甲 |

- 第 3 輪直接以組合甲建立投組診斷，不再要求指定投組。

### journey-01：財報 → 已指定投組影響 → 降低波動

Fixture：`standard`；3 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t2 | 這份財報對我的組合甲可能有什麼影響？ | portfolio_impact；組合甲；2330 |
| t3 | 那要如何降低這個組合的波動？ | risk_reduction；組合甲 |

- 從台積電 2025 年財報承接到組合甲曝險，最後完成有計算依據的風險方案比較。
- 不重問已指定的公司、年度或投組；每輪僅建立其必要 workflow。

### journey-02：缺少投組 → 澄清 → 恢復影響分析

Fixture：`standard`；4 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t2 | 對我的組合可能有什麼影響？ | clarify |
| t3 | 組合甲。 | portfolio_impact；組合甲；2330 |
| t4 | 如何降低波動？ | risk_reduction；組合甲 |

- 單獨回覆組合名稱後，恢復原財報影響任務，再承接降波動目標。
- t2 必須澄清；t3 不得再次詢問要分析什麼。

### journey-03：先選投組，再研究財報與降低波動

Fixture：`standard`；5 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 我有幾個投資組合？ | list_portfolios |
| t2 | 接下來看組合甲。 | select_portfolio；組合甲 |
| t3 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t4 | 這份財報對剛剛選的組合可能有什麼影響？ | portfolio_impact；組合甲；2330 |
| t5 | 如何降低這個組合的波動？ | risk_reduction；組合甲 |

- 公司研究不得遺失先前選定投組；後續影響與風險方案都綁定組合甲。
- t5 原稿為「如何降低它的波動？」，「它」可以指台積電或組合甲，標準答案有歧義，因此改為明確指稱。

### journey-04：降低波動途中切換投組

Fixture：`standard`；4 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t2 | 這份財報對組合甲可能有什麼影響？ | portfolio_impact；組合甲；2330 |
| t3 | 如何降低這個組合的波動？ | risk_reduction；組合甲 |
| t4 | 換成組合乙，同樣要降低波動呢？ | risk_reduction；組合乙 |

- t4 使用組合乙的實際 ID、持倉、權重和風險快照，保留降波動目標。
- 不能把組合甲的計算結果或使用者未指定的配置直接沿用到組合乙。

### journey-05：切換財報公司但保留投組

Fixture：`standard`；5 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t2 | 這份財報對組合甲可能有什麼影響？ | portfolio_impact；組合甲；2330 |
| t3 | 改看鴻海 2025 年財報：產品線營收占比如何，哪類年增率最高？ | research_answer；2317 |
| t4 | 那這份財報對同一個組合的影響呢？ | portfolio_impact；組合甲；2317 |
| t5 | 接著如何降低這個組合的波動？ | risk_reduction；組合甲 |

- 研究主體從台積電改為鴻海，投組仍為組合甲；t4 證據必須來自鴻海這輪。
- 降波動仍分析完整持倉，不把剛研究的公司自動當成唯一可減碼標的。

### journey-06：補齊投組後恢復原降波動目標

Fixture：`standard`；4 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t2 | 看完後想降低我的組合波動。 | clarify |
| t3 | 組合甲。 | risk_reduction；組合甲 |
| t4 | 先比較台積電降到30%、減碼款留現金，其他持股不變。 | risk_reduction；組合甲 |

- t3 單獨指定投組就足以恢復降波動任務，不能退回無目標的一般診斷。
- t4 遵守明確配置，與原組合用同一快照比較。

### journey-07：方案改成既有持股，再改回現金

Fixture：`standard`；5 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t2 | 對組合甲可能有什麼影響？ | portfolio_impact；組合甲；2330 |
| t3 | 要降低波動，先比較台積電降到30%，減碼款留現金，其他持股不變。 | risk_reduction；組合甲 |
| t4 | 如果那筆減碼款改轉入原本持有的0050呢？ | risk_reduction；組合甲 |
| t5 | 還是改回留現金，其他條件照舊。 | risk_reduction；組合甲 |

- 三個情境維持同一投組、原始基準和台積電 30% 目標，只改指定的餘額去向。
- 前一輪是假設模擬，不可視為已成交或以模擬持倉取代真實持倉。

### journey-08：沒有直接持股時，不捏造財報曝險

Fixture：`no_direct`；3 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t2 | 這份財報對組合甲可能有什麼影響？ | portfolio_impact；組合甲；2330 |
| t3 | 那還能如何降低這個組合的波動？ | risk_reduction；組合甲 |

- 明確辨識組合甲沒有台積電直接持股，不能捏造台積電持倉或減碼。
- 沒有 0050 成分權重資料時，只能表達間接曝險的可能性與未知程度，不能斷言零曝險或給出精確穿透比例。
- 最後以真正持有的資產計算風險方案。

### journey-09：持倉可讀，但歷史資料不足

Fixture：`short_history`；3 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t2 | 這份財報對組合甲可能有什麼影響？ | portfolio_impact；組合甲；2330 |
| t3 | 如何降低這個組合的波動？請比較前後風險。 | insufficient_data；組合甲 |

- 財報與已知持倉仍可支持有限度的曝險說明；歷史風險資料不足時正確限制回答。
- 未產生數值改善方案在此是預期行為，不計為過度拒答。

### journey-10：重置後重新確認投組，再恢復新目標

Fixture：`standard`；6 輪。

| 輪次 | 使用者原文 | 必要行為 |
| --- | --- | --- |
| t1 | 台積電 2025 年財報如何？請先看毛利率與基本 EPS。 | research_answer；2330 |
| t2 | 這份財報對組合甲可能有什麼影響？ | portfolio_impact；組合甲；2330 |
| t3 | 如何降低這個組合的波動？ | risk_reduction；組合甲 |
| t4 | 重置對話。 | reset |
| t5 | 那如何降低波動？ | clarify |
| t6 | 組合乙。 | risk_reduction；組合乙 |

- 重置清除語義上下文，t5 產生新的待補任務，t6 用新選的組合乙完成。
- 重置不等於刪除帳號投組；仍能列出該使用者的合法選項。

## 後續接上 Harness 的順序

1. **階段 1：** 建立多投組合成 fixture 與 runner，只執行 deterministic 檢查。先跑 journey-00、journey-04（切換）與 journey-10（重置）。在投組記憶修正之前，journey-00 預期在 t3 失敗，失敗分類為 `bug`。
2. 修正投組記憶後以同一 fixture 重跑，確認 journey-00 由失敗轉為通過，保存前後兩份結果。
3. **階段 2：** 接上 judge 與校準，semantic 檢查先以 shadow 模式報告。
4. 保存原始逐輪結果與 metadata，支援離線 rescore，以及在相同 fixture 與 scorer 下 compare；live eval 依既有隔離與預算政策執行。
