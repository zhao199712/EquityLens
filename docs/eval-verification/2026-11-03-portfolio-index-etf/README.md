# ETF 無提示優化單題驗證

2026-11-03，凍結版本 `c22268e84af36cbfb9e2fd61f6ec0668bf9e1caf`，prompt 18／method v3，deepseek-flash。只問一次「可以給我優化建議嗎？」，沒有提供 ETF、減碼標的或比例；沒有重跑、改答案或改評分器。

## 結果

- 原 scorer v2 自動檢查 **1/1**；額外 ETF 行為檢查通過。這是獨立補充題，不能當成原 14 題重跑或穩定性證明。
- Agent 主動將 9001 從 80% 降至 60%，一次計算轉入 20% 的 0050 與保留 20% 現金，9002／9003 不變。四個 loop turns、八次成功工具呼叫，沒有 Guard 拒絕，因此未觀察恢復能力。
- 同快照的原組合／ETF／現金年化波動為 24.33%／18.57%／18.27%，假設最大回撤 -11.05%／-8.80%／-7.84%。獨立 Python 重算風險數學、權重及 NAV 守恆通過，持股與交易記錄前後相同。
- **人工語意檢查未通過**：回答表格數字正確，但取捨段稱 ETF「VaR 與期望短缺的改善幅度小於留現金」。ETF VaR 改善約 0.63 個百分點，現金約 0.60，VaR 比較方向說反；ES 比較方向正確。現有數字追溯 gate 不驗證這種比較敘述，不能把自動 1/1 解讀為答案完全正確。
- 回答提出取捨並詢問風險偏好，沒有無條件推薦買入 ETF。實際回答見 [answer.md](answer.md)，完整原始軌跡在 responses.jsonl.gz。

## 隔離與費用

獨立 `equitylens_portfolio_etf_eval` 只複製原測試庫 schema，沒有複製原使用者或 runs。新建一個測試帳號；9001/9002/9003 為原 concentrated 合成價格，0050 另加 253 日完整合成還原價格、零持股，006208 缺資料為 Unavailable。所有價格不是實際行情，fixture.json 保存公式產生後資料與日期，meta.json 保存 fixture hash。基準 API 固定指數 100 並有 preflight。這不是正式站驗證。

獨立授權上限 US$0.05，五次 provider 呼叫（含路由）、保守費用上界 **US$0.0097443**，pending 0；歷史累計上界 US$1.3156488。budget.json／provider_usage.jsonl 保存記錄；未保存帳號 token。測後 API／費用 proxy 已停止。沒有合併 main 或部署。

同版本 CI [41639905538](https://github.com/zhao199712/EquityLens/actions/runs/41639905538) 已成功。這次僅新增驗證證據及文件，未改程式或 prompt。

後續比較方向修正及離線驗證見 [offline-fix.md](offline-fix.md)；上方保留原始真實 AI 結果，不回寫為通過。
