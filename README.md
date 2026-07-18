# EquityLens

> 一個整合投資組合帳務、績效分析、量化風險模型、財報處理與 AI 研究工作流的全端投資研究平台。

EquityLens 的目標不是只呈現持股與即時損益，而是將投資人的完整研究流程串在一起：

```text
交易與現金流
    ↓
投資組合估值與績效
    ↓
量化風險分析
    ↓
財報與研究資料
    ↓
AI 證據檢查、批判與答案修訂
```

> [!NOTE]
> 本專案仍在持續開發中，主要用於金融科技、量化風險分析與 Agentic Workflow 的工程實作與研究；部分進階功能仍在演進，不宣稱為 production-ready 交易系統。

---

## 專案動機

一般投資工具常將能力拆散在不同平台：

- 券商提供持倉與損益，但通常缺乏完整績效與模型分析。
- 財報網站提供公司資料，但不會與個人投資組合連動。
- 量化工具能計算風險，卻通常沒有交易帳務與研究脈絡。
- LLM 能產生研究內容，但若缺乏證據檢查、執行狀態與可觀測性，很難追蹤答案如何產生。

EquityLens 嘗試把這些能力放進同一條資料與研究流程中，並將「計算正確性、狀態管理、失敗恢復與可觀測性」視為核心工程問題，而不只是製作投資 Dashboard。

---

## 核心功能

### 1. 投資組合帳務與績效

以交易與現金流事件建立投資組合，而不是只儲存目前持股。

- 買入、賣出與持倉彙總
- 入金、出金與現金餘額
- 股息與投資組合估值
- 成本、損益與資產權重
- TWR（Time-Weighted Return）
- XIRR（Money-Weighted Return）

這個模型明確區分「外部資金流」與「投資組合內部資產轉換」，避免買入股票被錯誤視為新的投資報酬來源。

### 2. 量化風險分析

目前包含或持續整合的風險與統計能力：

- Volatility
- Value at Risk（VaR）
- Expected Shortfall（ES）
- Sharpe Ratio
- Maximum Drawdown
- Beta
- Correlation / Covariance
- Portfolio Variance
- GBM Monte Carlo Simulation
- EWMA-based risk estimation
- MVEWMA-FHS（Multivariate EWMA Filtered Historical Simulation）
- Covariance shrinkage

核心金融數學盡量抽離為可測試的純計算模組，降低「程式能執行，但金融口徑錯誤」的風險。

### 3. 財報資料管線

已完成台灣上市櫃財報資料取得的 PoC 與後續處理基礎：

- TWSE 財報查詢頁解析
- 財報 PDF 下載
- PDF 中文文字提取
- S3-compatible object storage
- 財報與研究資料持久化
- 後續檢索與 AI 研究流程的資料基礎

### 4. AI Research Workflow

研究流程不是單次 LLM completion，而是將研究品質檢查拆成可追蹤節點。

目前的 `ResearchQualityReview` 概念流程：

```text
Load Research Run
        ↓
Build Evidence Packet
        ↓
Check Evidence
        ↓
Critique Answer
        ↓
Finalize Critic Report
        ↓
Draft Revised Answer
        ↓
Finalize Revision
```

系統採用 constrained agentic workflow：

- 流程與安全邊界由程式控制
- LLM 負責需要語義理解的判斷
- 可形式化的驗證盡量由 deterministic code 處理
- 每個節點具有明確的輸入、輸出與狀態

這不是完全自由的 autonomous agent；設計目標是可測試、可追蹤與可限制。

---

## Agent Runtime 設計

### Blackboard

Workflow 使用 Blackboard 保存共享的結構化執行狀態，例如：

- question
- answer
- citations
- evidence packet
- evidence checks
- critic findings
- revised answer
- final output

每個 node 宣告需要與產生的資料，避免共享狀態退化成無約束的全域變數。

### Workflow Graph

Workflow definition 以節點與依賴關係描述。執行前由 planner 與 graph validator 驗證執行順序與依賴關係。

目前主要採 deterministic workflow graph；未來若加入「證據不足 → 重新檢索」等迴圈，會以 bounded loop 或明確的 state transition 控制，而不是讓 Agent 無限制自行循環。

### State Machine

Agent Run 與 Node 都具有明確狀態，例如：

```text
Pending → Running → Succeeded
                  ↘ Failed
```

狀態轉換集中管理，避免不同 service 任意修改狀態造成不一致。

### Asynchronous Execution

Agent workflow 已從同步 HTTP request lifecycle 拆離：

```text
Client
  │
  ▼
ASP.NET Core API
  │  Create Run + Enqueue
  ▼
Redis-backed Queue
  │
  ▼
Background Worker
  │
  ▼
AgentRunExecutor
  ├── Workflow Planner
  ├── Graph Validator
  ├── State Machines
  ├── Node Handlers
  └── Telemetry / Events
```

這個設計將 request lifecycle 與 job lifecycle 解耦，為長時間 LLM / tool execution、retry 與後續 worker 擴充提供基礎。

---

## 系統架構

```text
┌──────────────────────────────┐
│        Vue 3 Frontend        │
│ Portfolio / Risk / Research  │
└──────────────┬───────────────┘
               │ HTTP API
               ▼
┌──────────────────────────────┐
│      ASP.NET Core Web API    │
│                              │
│ Portfolio & Transactions     │
│ Performance & Risk           │
│ Financial Filings            │
│ Research & Agent Runs        │
└───────┬────────┬────────┬────┘
        │        │        │
        ▼        ▼        ▼
 PostgreSQL    Redis    Object Storage
 / ParadeDB    Queue       Garage
        │
        ▼
 Durable application state

Agent Run API
     │
     ▼
Redis Queue → Worker → Workflow Executor
                         │
                         ├── Node Events
                         ├── State Transitions
                         └── OpenTelemetry
```

---

## 技術棧

### Backend

- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL / ParadeDB
- pgvector
- StackExchange.Redis
- JWT Authentication
- S3-compatible object storage

### Frontend

- Vue 3
- Vite
- TypeScript / JavaScript ecosystem
- Component-based investment, risk and research UI

### AI / Agent

- Microsoft Agents AI
- Google GenAI integration
- Structured workflow execution
- Blackboard-based shared state
- Workflow planner and graph validation
- Run / node state machines
- Redis-backed background execution

### Data & Documents

- TWSE financial filing ingestion
- PdfPig-based PDF text extraction
- Garage object storage
- Vector-ready document pipeline

### Observability & Infrastructure

- OpenTelemetry
- Aspire Dashboard
- Docker Compose
- Redis 7
- PostgreSQL / ParadeDB
- Garage

---

## 代表性的工程問題

### 金融計算正確性

金融系統最危險的錯誤不一定會造成 exception；錯誤的數字往往仍然「看起來合理」。因此專案特別重視：

- 報酬率與現金流口徑
- simple return / log return 的使用情境
- VaR / ES 的信賴水準與持有期間
- 年化方式
- 投資組合權重與共變異數
- 邊界條件與資料不足時的處理

### Queue 與冪等性

背景工作不假設 exactly-once delivery。設計方向是以唯一 `runId`、資料庫狀態與合法 state transition 防止重複執行。

若未來部署多個 worker，仍需要進一步加入 atomic claim、optimistic concurrency 或其他並行控制機制。

### LLM 可靠性

LLM Critic 不是正確性的形式化證明。系統的方向是：

- deterministic checks 處理可形式化規則
- LLM 處理語義判斷
- evidence packet 限制研究上下文
- structured output 降低格式不確定性
- event / telemetry 保留執行軌跡

---

## TWSE 財報爬蟲 PoC

早期 PoC 已驗證：

| 項目 | 狀態 | 說明 |
|---|---|---|
| 成分股資料取得 | ✅ | 取得股票清單資料 |
| TWSE 財報頁面解析 | ✅ | 解析財報查詢結果 |
| PDF 下載 | ✅ | 取得並保存財報 PDF |
| 中文文字提取 | ✅ | 使用 PdfPig 處理中文財報 |

財報抓取 API 範例：

```http
POST /api/financial-filings/crawl
Authorization: Bearer {token}
Content-Type: application/json
```

```json
{
  "stockCodes": ["2330", "2498"],
  "startYear": 112,
  "endYear": 115
}
```

---

## 本機基礎設施

Docker Compose 提供主要基礎服務：

```bash
docker compose up -d
```

預設包含：

- PostgreSQL / ParadeDB
- Redis
- Garage object storage
- Garage Web UI
- Aspire Dashboard / OTLP endpoint

> 實際啟動完整 API 與前端前，仍需依本機環境設定資料庫連線、JWT、外部市場資料來源與 AI provider credentials。

---

## 測試策略

專案的測試方向包含：

- 金融數學與投資組合計算的 unit tests
- Workflow graph validation
- State transition tests
- Agent node contract / handler tests
- Queue、Worker 與 persistence 的 integration testing
- LLM structured output 與研究品質評估

對 LLM 輸出不以完整字串相等作為唯一判斷，而應驗證 schema、必要事實、citation grounding 與品質指標。

---

## 已知限制

目前仍在持續演進的部分包括：

- Agent runtime 尚未宣稱具備完整 distributed exactly-once semantics
- Multi-worker concurrency control 仍可進一步強化
- Workflow version replay / migration 尚需完善
- VaR backtesting 與模型檢定仍在持續整合
- 部分壓力測試與模擬功能可能仍屬研究或示範階段
- AI 研究品質仍需要 golden dataset 與更系統化 evaluation
- README 的 UI screenshots 與正式架構圖仍待補充

---

## Roadmap

### Portfolio & Risk

- [ ] 完整 VaR rolling backtest
- [ ] Breach / exception sequence
- [ ] Kupiec unconditional coverage test
- [ ] Christoffersen independence test
- [ ] 更完整的 stress testing

### Research

- [ ] 強化財報 chunking 與 retrieval
- [ ] Citation-to-claim validation
- [ ] Research evaluation dataset
- [ ] Evidence quality scoring

### Agent Runtime

- [ ] Atomic job claim / stronger concurrency control
- [ ] Dead-letter handling
- [ ] Node-level retry policy
- [ ] Workflow versioning and replay strategy
- [ ] Cost / token / latency evaluation dashboard

### Project Presentation

- [ ] UI screenshots
- [ ] Formal architecture diagram
- [ ] Reproducible development setup
- [ ] Demo dataset and walkthrough

---

## 設計原則

EquityLens 的核心工程原則：

1. **交易帳務是事實來源，持倉是推導結果。**
2. **金融模型必須說明假設、資料口徑與限制。**
3. **可由程式驗證的規則，不交給 LLM 猜。**
4. **讓 LLM 處理語義判斷，讓程式控制流程與安全邊界。**
5. **長時間工作與 HTTP request lifecycle 解耦。**
6. **Agent 的中間狀態、失敗與決策必須可觀察。**
7. **不把 prototype 包裝成 production-ready system。**

---

## 專案定位

EquityLens 目前最準確的定位是：

> **Portfolio Analytics + Quantitative Risk + Financial Research + Constrained Agentic Workflow**

它不是券商交易系統，也不是完全自主的投資 Agent；它是一個用來探索金融資料、量化風險、研究流程與可靠 AI orchestration 如何整合的全端工程專案。
