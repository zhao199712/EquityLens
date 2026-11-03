# Portfolio acceptance：修正與真實模型驗證

交付分支 `fix/portfolio-quality-eval`，基準 `7f9fc35`。沒有部署、修改正式資料或變更研究原 8 題計分範圍。

## 結論

最近完整真實模型輪次為 **8/14**，其中無提示「可以給我優化建議嗎？」通過。9 題要求情境的題目中 8 題成功計算；有足夠資料的情境皆通過 Python 獨立重算，14 題持倉／交易前後一致。這不代表所有 AI 建議都通過：數學正確與完整回答品質分開計分。

最後又修正了表頭引用與多情境摘要截斷，離線測試通過；但最新程式的真實 AI 驗收遭供應商連續 503 阻塞，**不能把 8/14 當成最新程式已完成的驗收分數**。

| 輪次 | 嚴格分數 | 說明 |
|---|---|---|
| 1 | 5/14 | 初次修正後驗證，歸因使用外部基準 |
| 2 | 6/14 | 格式與上下文修正，仍使用外部基準 |
| 3 | 8/14 | 有界回答修正，仍使用外部基準 |
| 4 | 7/14 | 全合成行情與固定 100 基準 |
| 5 | 8/14 | 工具提供固定格式顯示值，無提示優化通過 |
| 6 | 不計品質 | 隔離 worktree 缺 JWT audience，沒有模型呼叫 |
| 7 | 不計品質 | 供應商 503，人工停止；未結算費用全額保留 |
| 8 | 不計品質 | 503，自動停止；1 題失敗、13 題未跑 |

這是開發過程各版本的紀錄，沒有重構前的投組品質基準，不是正式新舊版本 A/B。前三輪與後續輪的基準行情不同，不能合併計算平均分數。模型觀察值為 `deepseek-flash`；第五輪逐題結果見 [report](round-5/report.md)。

## 驗證與限制

- 後端 949 通過（18 個真實 PostgreSQL 整合，0 skipped）；風險跨語言契約及 DB → C# → Redis → Python → DB 5 通過。
- 前端 build、138 Vitest、9 Playwright smoke 通過。瀏覽器 smoke 使用固定 API 回應，不是正式站真實 AI E2E。
- Mathematics 36、eval harness 90 通過；EF 無 pending-model changes。
- recovery 未發生即為 null。本次真實情境工具呼叫沒有可供評估的拒絕後恢復軌跡；另有注入式模型回歸測試，兩者分開揭露。
- scorer v2 另外識別 provider error，並把無資料、無法獨立重算的數學檢查留作未評估，不把空集合當通過。題目、正確期望與研究分母未改；歷史回答離線重新計分，未重問模型。
- 數字敘述仍使用程式品質關卡，並非獨立語義 judge。格式修復不保證投資建議的適切性；真實市場資料、交易費用與未來收益不在本合成測試涵蓋範圍。

## 成本與可重現資料

使用者授權本輪總額 **US$1**；所有請求、失敗及未結算保留額合計上界 **US$0.498177**。逐輪 ledger 與 budget 保留，並未重新設定總額。供應商錯誤的預留額不代表實際扣款，但計入保守上界。

各 `round-*` 目錄保存 metadata、summary、report、費用資料與 `responses.jsonl.gz`：其中包含原始回答、持久化工具軌跡、審批前草稿及完成結果、持倉與交易前後快照。僅隔離合成帳號；不包含 token、密碼或 API key。原始 SHA256 保存在 verification.json。完整 logs 壓縮保存。

```bash
gunzip -c round-5/responses.jsonl.gz > /tmp/portfolio-responses.jsonl
```

執行方式及隔離／費用規則見 [portfolio-eval](../../testing/portfolio-eval.md)。PR review 後先重跑完整模型驗收，檢視品質降級與 Router 澄清；通過再依既有手動 CD 流程 verify／核准部署。本次不執行 merge 或 deployment。
