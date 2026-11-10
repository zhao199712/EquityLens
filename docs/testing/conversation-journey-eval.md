# 財報到投組決策：多輪評估集 v1

這份評估集沿著「某家公司財報如何 → 對我的投組可能有什麼影響 → 如何降低波動」驗收連續任務，並以 journey-00 重現 2026-11-04 正式站回報的投組記憶問題。

**目前狀態：** 已實際執行 GPT Luna 的 11 組、45 輪隔離測試。評分器 v2 將獨立事實／計算檢查、產品 Quality Gate 觀測及待覆核語義分開報告，詳見 [多輪評估器 v2](conversation-scoring-v2.md)。所有案例仍為 draft，尚無人工校準的語義評分器，不能宣稱完整對話通過。

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
| `capability_gap` | 產品尚未支援的能力。目前沒有列入此類的項目；研究證據傳遞已實作，`research_to_exposure` 留待語義驗證。 |
| `dependency_failed` | 前一輪（通常是研究）失敗，導致下游證據斷言無法成立。根因記在前一輪；後輪不得用標準答案補洞，整段不通過。 |

## Fixture 與資料就緒狀態

每個案例使用 owner_a 的兩個投組「組合甲／組合乙」，另設 owner_b 的同名投組作為隔離干擾。名稱不暗示哪個波動較高。

| Profile | 組合甲 | 組合乙 | 說明 |
| --- | --- | --- | --- |
| `standard` | 2330 60%、2317 25%、0050 15% | 2330 20%、2317 30%、0050 50% | 253 個價格點 |
| `no_direct` | 2317 60%、0050 40% | 2317 30%、0050 70% | 組合甲沒有 2330 直接持股；不提供 0050 成分權重 |
| `short_history` | 同 standard | 同 standard | 61 個價格點，少於風險分析所需的 121 個共同交易日 |

這些由 seed 產生的合成行情規格，**不是已 seed 的資料或實際投組**。既有 `portfolio_seed` 每個帳號只有一個投組，不能直接拿來執行本題庫。同一個 ticker 在資料庫中只能有一筆 security，研究標的與持股要解析到同一筆；不同投組共用同一個價格序列。

**as_of 政策（`seed_date_frozen`）：** 產品的診斷期間預設以執行當天往前推一年，因此行情日期取 seed 當天的 UTC 日期，並寫入私有 manifest 凍結。同一天的不同版本可以共用 fixture；跨日期 live 重跑必須重新 seed，fixture hash 不同時，不得宣稱回歸改善。已保存的結果可以隨時離線重評。

規格檔的 `numeric_golden` 保持 `null`，validator 拒絕預填。Seed 依實際四捨五入後的股數獨立計算權重、NAV、波動、VaR、ES、回撤與指定情境，放在產生的 manifest，並記錄 `numeric_verification`。`review_status` 另表示人工核實題目與成功條件；兩者分開記錄，seed 不會自動把 draft 升級為 verified。

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

每項斷言保存 `pass`／`fail`／`unverifiable` 與證據位置，失敗時另存失敗分類。必要證據缺失不得算 pass。完整對話通過需要所有必備條件與適用的語義條件成立；身分錯誤、越權或副作用任何一項發生即失敗，不能被答案品質平均掉。

目前 PortfolioDiagnosis v5 沒有人工審批節點；保存產品 Quality Gate 與最終回答，並分開報告。Runner 的舊唯讀 WorkflowGate 相容處理不能處理交易或 ToolCall 授權。

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
| `research_to_exposure` | semantic | 前輪有來源的財報事實與本輪讀取的持倉形成可追蹤的影響說明，不能只檢查公司關鍵字。 |
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

1. **階段 1（已實作）：** 多投組合成 fixture、runner 與 deterministic 檢查。先跑 journey-00、journey-04（切換）與 journey-10（重置）。在投組記憶修正之前，journey-00 預期在 t3 失敗，失敗分類為 `bug`。
2. 修正投組記憶後以同一 fixture 重跑，確認 journey-00 由失敗轉為通過，保存前後兩份結果。
3. **階段 2：** 接上 judge 與校準，semantic 檢查先以 shadow 模式報告。
4. 保存原始逐輪結果與 metadata，使用離線 rescore 比較同一批回答；跨產品版本報告比較必須核對題庫、fixture、語料與模型條件。本階段沒有新增自動 compare 命令。

## 執行 seed、run 與 rescore

### 環境與隔離

先依 [portfolio-eval.md](portfolio-eval.md) 建立已遷移的隔離服務。每個 profile 使用不同的資料庫、API 連接埠和 Redis／快取命名空間；資料庫名稱必須以 `_eval` 結尾。資料庫容器的 Compose project label 限 `equitylens-runtime`、`equitylens-portfolio-eval`、`equitylens-conversation-eval`。停用行情同步工作，避免合成價格被外部行情替換；行情、持倉與現金都會在執行前核對，行情另於每個案例後複查。

Seed 可重用語料快照既有的單一 security，但 ticker／exchange／currency／asset_type 必須一致，且該 security 不得有行情。已 seed 的資料庫不能再次 seed；不會刪除既有價格或投組。每個案例建立自己的 owner_a／owner_b，因此同日重測另一個產品版本也要準備另一份隔離資料庫與新帳號；相同日期／版本的生成器會得到相同 fixture hash。帳號已出現 Session 時 runner 拒絕重用。

```bash
cd evals
python -m pip install -e '.[test]'
python -m equitylens_eval.conversation validate
python -m pytest

# 範例：僅建立 standard profile；路徑放在 repository 外。
python -m equitylens_eval.conversation seed \
  --profile standard --base-url http://127.0.0.1:5535 \
  --container journey-standard-db --database journey_standard_eval \
  --username equitylens --manifest /tmp/journey-standard.private.json
```

Seed 的 `as_of` 預設為當日 UTC，live seed 不接受其他日期。私有 manifest 使用 0600 權限，含臨時帳號密碼，絕不可提交。它在註冊帳號前以獨占方式建立；失敗時留下 `state: seeding` 與已建立帳號供診斷，不可交給 runner。使用新環境重新 seed，不自動清除失敗環境。

### 研究語料

journey-00 不依賴財報，可以不提供語料。其餘案例需要已核實、已匯入且完成 embedding 的語料快照；seed 不下載或捏造財報、不建立 embedding。將現有核實紀錄對應的文件寫成 corpus proof，再以 `seed --corpus /tmp/corpus-proof.json` 指定：

```json
{
  "version": "reviewed-corpus-2026-11-02",
  "case_refs": [
    "2330-gross-margin-2025",
    "2330-eps-2025",
    "2317-segment-mix"
  ],
  "documents": {
    "<isolated-DB-document-UUID>": "<uploaded-file-SHA256>"
  }
}
```

文件 ID／雜湊必須填入隔離資料庫中的實值，`case_refs` 必須對應題庫的已核實題目；proof 是人工核實聲明，不會因為雜湊相同就替代人工核實。Harness 保存文件中繼資料、chunk 內容與 embedding 指紋，執行前重新比對。不提供 proof、未涵蓋該案例引用，或語料缺失／變更時，該案例記為 `dependency_failed`，不送出聊天請求。

### Live 執行

先配置外部 provider 預算限制，涵蓋 Router、Agent、Critic、embedding、reranker、搜尋等實際使用服務。`CONVERSATION_EVAL_BUDGET_ENFORCED=1` 是操作者對外部設定的聲明，**本 harness 不提供每月 US$100 的跨服務硬上限**。它沿用 budget proxy ledger 的 `reserved_usd`／`charged_upper_usd`／`status` 格式，偵測 provider 錯誤或未結算請求後停止。報告不把 Agent token 小計冒充總帳單。

```bash
# API_SHA 是實際被測 API 的 build SHA。
CONVERSATION_EVAL_BUDGET_ENFORCED=1 \
python -m equitylens_eval.conversation run \
  --manifest /tmp/journey-standard.private.json \
  --case journey-00 --include-unverified \
  --system-commit "$API_SHA" \
  --usage-ledger /tmp/provider_usage.jsonl \
  --out /tmp/journey-run-01
```

先驗證 journey-00，再以已備妥語料的環境加入 `--case journey-04 --case journey-10`。執行全部案例時，省略 `--case`，並重複指定三份 profile 的 `--manifest`。預設每輪逾時 360 秒，可用 `--timeout` 調整。所有題目目前為 draft，須明確帶 `--include-unverified` 才會執行。

每輪等待真實 Run 完成後才繼續。只自動核准 PortfolioDiagnosis 的 `approvePortfolioDiagnosis / WaitForHumanApproval / WorkflowGate / ReadOnly`；先保存審批前的 draft，再取得完成結果。未知審批、執行失敗、SSE 不完整或逾時會停止該案例並嘗試取消已建立的 Run；後輪標為 `not_run`。取消未確認時停止整批，避免持續送入新案例。非預期澄清本身仍會保存並依固定腳本繼續，不臨時補問法。

### 證據與離線重評

輸出目錄必須是新目錄，包含：

| 檔案 | 內容 |
| --- | --- |
| `responses.jsonl` | 各案例原始逐輪事件、回答、Run／工具／Quality Gate、Session／ConversationTurn／Run input，以及前後持倉快照 |
| `checkpoints.jsonl` | 每個 SSE 事件與輪次完成時的增量 checkpoint；中斷後仍可人工檢查 |
| `cases.json`、`research.json`、`fixtures.json` | 本次使用的題目與評分依據；fixture 已去除帳號憑證 |
| `run_meta.json` | 被測 API SHA、harness SHA／來源雜湊／dirty 狀態、題庫與 fixture 雜湊 |
| `score_meta.json` | scorer 版本與來源；重評時另記原始目錄 |
| `summary.json`、`report.md` | 逐項證據位置、結果、失敗分類、程式檢查覆蓋率及完整對話狀態 |

```bash
python -m equitylens_eval.conversation rescore \
  --responses /tmp/journey-run-01/responses.jsonl \
  --out /tmp/journey-run-01-rescored
```

Rescore 使用保存的題庫、研究 oracle 與 fixture，驗證它們的雜湊；不接觸 API／資料庫／模型，不改原始結果或 API SHA。不能用改動過的 fixture 宣稱相同條件下改善。`run`／`rescore` exit code：0 表示沒有觀測到 fail 且所有選定案例已處理，1 表示有 fail 或未執行案例；**exit 0 不代表完整對話已通過**，仍須查看 `unverifiable` 和驗證覆蓋率。

### 本階段評分界線

- Run input、黑板與工具結果中任何投組 ID 不符即失敗；只有文字名稱正確不能通過。缺少有效工具 scope 則為 unverifiable。
- 比較必須符合 seed 凍結的價格／持倉／NAV，獨立重算 sample volatility、95% historical nearest-rank VaR、ES 與假設回撤。現金零報酬、年化 252；風險數字容差 1e-7、資產金額容差 0.01。明確條件必須由同一個情境全部滿足。
- 研究程式檢查重用已核實數值、來源命中與引用索引檢查；不宣稱驗證了整段回答的語意、來源角色或未來預測。
- 列舉的回答沒有結構化 portfolio IDs、Router 實際歷史輸入沒有持久化，因此 `owned_portfolio_list`／`reset_boundary` 不能僅因名稱出現或 context 清空就算通過；已知錯誤仍可判 fail。
- 所有 semantic checks 保留 unverifiable。回歸錯誤、能力缺口與依賴失敗分開統計；未知不補零。Draft 不進完整成功率，必要項目未驗證時顯示 N/A。

離線測試使用假的 HTTP/SSE／資料庫介面，涵蓋全 45 輪、錯誤資源綁定、金額與風險計算反例、審批與逾時、seed 防重複與 corpus 指紋、報告與離線重評。這些是 harness 的測試，不代表真實 Agent 已通過 11 組案例。

2026-11-04 已補做 GPT-6 Luna 的 journey-00 真實整合測試，修正審閱節點契約後三輪完成；11 項通過、1 項 unverifiable。測試範圍、兩次成本與限制見 [實測紀錄](../eval-verification/2026-11-04-conversation-harness/README.md)。
