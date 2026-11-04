# OpenAI GPT-6 Luna 隔離接入驗收

日期：2026-11-04（台灣）。本次只切換隔離 API，正式環境與預設 DeepSeek 均未變更。
工作分支 `codex/gpt6-luna-provider` 以 PR #20 的 `d65533b` 為基準；P3 問卷尚未完成的工作留在原工作目錄，未帶入本分支。

## 接入方式

新增 `OpenAiChatCompletionService` 實作既有 `IChatCompletionService`，使用 Responses API。
模型使用 `gpt-6-luna`，推理強度預設 `low`。既有 Router、Agent JSON 協定、工具 Guard、品質關卡與審批不變。
`store=false`；只解析 `final_answer`（相容缺省／null phase），不把 commentary 當成工具決策或最終答案。
供應商 HTTP 錯誤不包含原始回應；缺用量、拒絕、未完成與空白回應均拒絕使用。輸出與推理共用 max_output_tokens。
未傳送 DeepSeek 專用 thinking 與跨模型不通用的 temperature。

在隔離 API 的環境設定（不要提交金鑰，不修改 appsettings.json）：

```sh
export AI__ChatProvider=OpenAI
export OpenAI__Model=gpt-6-luna
export OpenAI__ReasoningEffort=low
export OpenAI__BaseUrl=https://api.openai.com/v1
# OPENAI_API_KEY 由私有環境注入，也支援 OpenAI__ApiKey。
export AgentRuntime__ResearchAgentLoop__InputUsdPerMillion=0.225
export AgentRuntime__ResearchAgentLoop__OutputUsdPerMillion=0.5
```

輸入費率 0.225 為保守預算設定（一般輸入 0.10，加上最多 0.125 快取寫入預留），不是實際報價；不預先計入快取讀取折扣。每百萬 token、短 context、Standard；測試代理另拒絕長 context、額外內建工具或其他 service tier，未知回應扣完整預留。

移除 AI__ChatProvider 或設回 DeepSeek 可恢復原供應商，不涉及資料庫回復。切換供應商不影響外部 API key 的帳務來源；本次尚未核實 key 所屬 organization 是否已領 Pro credits。

## 驗證範圍

- 新增轉接器與 DI 測試；原始多 phase 問題的兩個重現測試先失敗，再修正通過。
- 完整後端測試與真實隔離 PostgreSQL，無略過。
- 兩題使用既有題目、合成行情與原評分器，從 chat API 經 Router、PortfolioDiagnosis、工具、品質關卡、隔離報告審批到最終輸出。
- 不代表原始 14 題全套 eval、正式財報檢索或長期穩定性已通過；尚未替換正式服務。
- 不調整提示詞、題目、品質規則或評分器迎合 Luna。

## 初次接入問題

前兩輪 none 模式各 0/2；供應商 HTTP 成功、路由成功，但初版轉接器將 commentary 與 final_answer 串接，造成 InvalidJson，工具未執行。
第二輪保留只有合成資料的原始回應，確認 commentary 內甚至包含候選工具 JSON，而 final_answer 為另一個物件。
修正只消費最終訊息，改用 low 推理再驗證。兩項同時改動，因此不將後續改善全部歸因於推理強度。

## 最終結果

| 問題 | 結果 | Run 耗時（含隔離審批） |
|---|---|---|
| 可以給我優化建議嗎？ | 通過；主動減碼情境，守恆、獨立數學與品質檢查通過 | 19.51 秒 |
| 這個波動率如何 | 通過；首段回答並引用，沒有無關情境段落 | 10.19 秒 |

最後一輪 2/2，每題各 3 輪 Agent 決策，沒有解析失敗、沒有變更持倉。無 ETF 證券資料的合成環境只測留現金方案，不能解讀為 ETF 候選驗收。

三輪供應商費用以每百萬 input $0.225、output $0.50 統一保守估算合計 **US$0.01468522**；最終一輪 **US$0.00708090**。非帳務實扣，未核實是否抵免費 credits；先前單次連線 smoke 另計約 US$0.0000171。第一輪代理的原估算未另加快取寫入預留，這裡重新計算更保守總額，原始紀錄保留。

- OpenAI 轉接器與註冊：16 項通過。
- 最終版本後端全套：1112 通過，0 失敗，0 略過（包含隔離 PostgreSQL）。
- 建置成功，既有 NU1510、xUnit 分析警告；離線還原時 NU1900 為無法讀取套件漏洞來源。
- 未修改前端，未執行前端測試。

已整理答案、LLM／工具摘要、所有輪次分數與供應商用量至 [驗證資料](../eval-verification/2026-11-04-luna-smoke/)。完整原始回應留於隔離工作目錄 exports/qa/luna-smoke-20261104*；不提交含登入憑證的 seed manifest。

官方接入依據：[Luna](https://developers.openai.com/api/docs/models/gpt-6-luna)、[Responses API](https://developers.openai.com/api/docs/guides/text)、[Pro API credits](https://developers.openai.com/api/docs/guides/chatgpt-api-credits)。

## 後續完整 14 題

同版單輪完整驗證為 **8/14**，不建議正式切換；詳見 [逐題結果與接入問題](openai-luna-full14.md)。前述 2/2 是先前兩題 smoke，不能代表全套穩定性。

原生工具接入後，修正版本兩輪離線 v4 分數為 12/14、12/14；詳見 [原生協定修正與完整驗證](openai-luna-native-tools.md)。仍未達切換門檻，正式服務不變。
