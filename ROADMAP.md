# 投資組合模擬與財務分析平台 Roadmap (V1~V4)

> AI-assisted Portfolio Risk & Financial Analysis Platform
> 建議開發順序：V1 → V2 → V3 → V4

---

## V1 — MVP / 最小閉環

**定位**：MVP / 最小閉環

### 前台功能
- Dashboard
- Portfolio List
- Portfolio Detail
- Risk Run Detail
- Financial Report Detail

### AI / 分析能力
- Historical VaR / ES
- 基本 Scenario
- AI Memo
- Citations
- Critic Notes

### 後台 / 系統能力
- 使用者管理
- 股票標的管理
- 市場價格管理
- 財報資料管理
- Risk Model 設定
- AI Report 設定
- Job 狀態管理

### 不包含 / 之後再做
- 完整 RAG
- Agent Runs
- Prompt 管理平台
- MiniIO / Meilisearch
- SaaS 營運功能

**版本演進重點**：先把 Portfolio / Risk / Report 閉環做完。

---

## V2 — AI / RAG 強化版

**定位**：AI / RAG 強化版

### 前台功能
- 在 V1 基礎上新增
- Company Research
- Report List
- Filing Chunks 檢視
- Portfolio Impact 更完整

### AI / 分析能力
- Chunking
- Embedding
- pgvector / Vector Search
- RAG 問答
- Support Level
- 更完整 Citation
- 批判檢查加強

### 後台 / 系統能力
- Chunk 管理
- Embedding 狀態
- RAG 設定
- 資料品質檢查
- Report 評估資料

### 不包含 / 之後再做
- 完整 Agent 平台化
- Skill Registry
- SaaS Billing

**版本演進重點**：讓 AI 回答基於可檢索資料，而不是憑空生成。

---

## V3 — Agent / 平台化版

**定位**：Agent / 平台化版

### 前台功能
- Agent Run List
- Agent Run Detail
- Execution Timeline
- Skills / Tool Calls
- 更多 Explainability 畫面

### AI / 分析能力
- Financial Analyst Agent
- Risk Analyst Agent
- Critic Agent
- Workflow Orchestration
- Execution Trace
- Human Feedback Loop

### 後台 / 系統能力
- Prompt 管理
- Skill Registry
- Agent 管理
- Tool Call Logs
- Failed Jobs Retry
- Redis 任務狀態與快取

### 不包含 / 之後再做
- 完整多租戶營運
- Billing
- 使用量計費

**版本演進重點**：把 AI 從單次報告生成，升級為可觀測的工作流系統。

---

## V4 — SaaS / 營運版

**定位**：SaaS / 營運版

### 前台功能
- 多投資組合管理
- Watchlist
- 權限角色
- Export / 分享
- 搜尋與篩選體驗強化

### AI / 分析能力
- 版本化 Prompt
- 模型切換
- 報告比較
- 品質評估 Dashboard
- 使用者回饋統計

### 後台 / 系統能力
- 多租戶
- API Key 管理
- Billing
- Usage Metrics
- MiniIO Artifact Storage
- Meilisearch / Search
- 完整營運後台

### 不包含 / 之後再做
- 進一步商業化功能

**版本演進重點**：從 side project 變成可營運、可擴充的產品。

---

## 目前進度

| 階段 | 狀態 |
|------|------|
| V1 | 已完成 |
| V2 | 已完成（含 trace persistence + step tracking） |
| V2→V3 前置 | 進行中 |
| V3 | 下一步：Critic Agent v0 |
| V4 | 尚未開始 |
