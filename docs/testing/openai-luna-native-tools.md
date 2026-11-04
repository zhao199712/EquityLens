# Luna 原生工具協定修正

日期：2026-11-04。僅隔離驗證，正式服務與預設 DeepSeek 不變。P3 問卷工作仍暫停。

## 改動

- OpenAI Responses 註冊當輪可見的任務工具，使用 strict function schema；submit_answer／give_up 是迴圈終止控制動作，不是額外領域能力。
- 轉接器只翻譯 function_call，不執行工具、不把 commentary 當指令。與最終動作混合的工具批次整批拒絕。
- 既有 loop 先保存決策與供應商續接訊息，再經 Guard 執行；保存每筆結果後才加入 function_call_output，原 call_id 隨 checkpoint 恢復。store=false 時保存並續傳加密 reasoning，不使用供應商儲存的 response ID。
- 強制 submit_answer 提供 markdown 與 citedEvidenceIds；原有 LoopProtocol、數字／引用品質檢查仍再次驗證。schema 完整不代表內容正確。
- strict schema 的選填欄位轉 nullable required；任意 key 的權重字典只在傳輸層改成 key/value 陣列，進 Guard 前還原，重複 key 拒絕。風險數學與授權不變。
- 本地預算預留包含實際工具 schema、歷史訊息與最大輸出；forced draft 隱藏領域工具。DeepSeek 沿用原 JSON 協定及提示。
- 評分器 v3 只補「不太合適／不太適合」的同義表達及否定反例；score(..., scorer_version=2) 可重現舊分數。原始 8/14 報告不覆寫，後續會並列同一回答的 v2/v3 分數。

## 驗證

測試涵蓋原生呼叫與文字分離、必填引用、權重還原／重複 key、續傳 reasoning 與 call_id、預算、Guard 拒絕後修正、研究與投組中斷恢復、審批釋放租約／核准／拒絕／冪等。
實際移除原生回應映射，新增測試失敗；恢復後通過。移除評分器同義詞修正，兩個正例失敗、四個反例通過；恢復後通過。

後端 build 成功；完整後端 1125 passed / 0 failed / 0 skipped（包含真實隔離 PostgreSQL）；eval pytest 101 passed。既有 NU1510、NU1900 與 xUnit 警告保留。

真實 AI：使用者授權本輪總上限 US$50（更正先前誤解的 0.20）；以費用代理記錄並攔截。尚未核實目前 API key 是否適用 Pro credits，成本為保守估計，不宣稱免費。預定同一版本完整 14 題兩輪，報告保留所有失敗與未跑題。

無 migration、無 appsettings.json 修改、無前端變更；不合併 main、不部署。
