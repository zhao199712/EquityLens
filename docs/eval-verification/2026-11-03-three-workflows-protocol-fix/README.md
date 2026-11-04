# 協定修正後的中間驗證

測試版本 `9f60065`；沿用原 8 題、隔離資料、關閉 reranker／thinking／judge。原始題庫與 scorer 不變。本輪 **6/8**，仍未達舊基準 7/8，保留此輪全部結果，不以後續結果覆蓋。

- 鴻海產品線題恢復通過，研究與修訂未再發生 JSON 解析錯誤。
- EPS 題正確回答 66.26 元，但附加淨利與加權平均股數。數字 gate 無法驗證來源未明示的淨利單位，也不支援列名「仟股」，因此拒絕答案。Critic 接受答案，而修訂仍原樣保留阻擋數字；這不是缺少 EPS 來源。
- 保證獲利題已提供有效引用，citation_integrity=1；但「無法保證……一定會賺錢」的否定句命中既有禁詞 `一定會賺`，keywords=0。這是詞彙評分限制，不代表本輪回答承諾獲利。未改 scorer／標準答案。

後續修正：股數單位只作用於明確列名後的數值，且不可套用到金額或 EPS；對無單位的淨利仍要求補來源或移除非必要附加數字。prompt 明示數字 gate 阻擋優先處理，風險說明避免複述承諾前提。

用量與預算見 [budget.json](budget.json)、[provider_usage.jsonl](provider_usage.jsonl)。本輪 LLM 保守估價 US$0.063004；另保留 embedding US$0.10。連同上一輪保守上界合計 US$0.315680，仍在使用者同一 US$1 授權內。

逐題比較：[comparison.json](comparison.json)。原始回答：[responses.jsonl](responses.jsonl)。持久化診斷：[traces.jsonl](traces.jsonl)。本輪本身沒有通過 R26，不作為最終驗收。
