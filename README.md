# EquityLens

> 整合投資組合帳務、績效分析、量化風險模型、財報處理與可控 AI 研究工作流的全端投資研究平台。

EquityLens 的目標不是只顯示持股和即時損益，而是把投資人的資料、計算與研究流程串成一條可追蹤的工程管線：

```text
交易與現金流
    ↓
投資組合估值與績效
    ↓
量化風險計算
    ↓
財報與研究資料
    ↓
AI 證據檢查、批判與答案修訂
```

> [!IMPORTANT]
> 本專案仍在持續開發，主要用於金融科技、量化風險與 Agentic Workflow 的工程實作。它不是券商交易系統，也不應被視為 production-ready 的投資建議服務。

---

## 核心能力

### 投資組合帳務與績效

以交易與現金流事件作為事實來源，而不是只儲存目前持倉。

- 買入、賣出與持倉彙總
- 入金、出金與現金餘額
- 股息、成本、市值與未實現損益
- 資產權重與投資組合估值
- TWR（Time-Weighted Return）
- XIRR（Money-Weighted Return）

這個模型明確區分外部資金流與投資組合內部資產轉換，避免把買入股票誤判為新增報酬。

### 量化風險分析

目前包含或持續整合的模型與統計能力：

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
- MVEWMA-FHS
- Covariance shrinkage

金融數學由獨立 Python worker 執行，透過 Redis Streams 與 API 協作。核心計算盡量維持為可測試的純函式，避免「程式成功執行，但金融口徑錯誤」。

### 財報資料管線

- 台灣上市櫃財報查詢頁解析
- 財報 PDF 下載與保存
- 中文 PDF 文字提取
- S3-compatible object storage
- 財報與研究資料持久化
- 向量檢索與 AI 研究流程的資料基礎

### AI Research Workflow

研究流程不是單次 LLM completion，而是由受限制、可驗證、可觀察的節點組成。

```text
Router
  ↓
Lead Skill
  ↓
Planner 建立受限 DAG
  ↓
Graph / Contract Validator
  ↓
Worker 執行 Nodes
  ↓
Evidence Check → Critique → Revision → Final Output
```

設計原則：

- 程式負責流程、安全邊界與 deterministic validation
- LLM 負責語義理解、規劃、批判與文字生成
- Capability 必須位於 Skill 授權範圍內
- Node 宣告輸入、輸出、參數與 Blackboard contract
- Run、Node、tool call、成本與事件皆可追蹤
- 已完成的 Run 視為不可變執行紀錄；feedback 會建立新的 Run

這不是完全自由的 autonomous agent，而是 constrained agentic workflow。

---

## 系統架構

```text
┌──────────────────────────────┐
│        Vue 3 Frontend        │
│ Portfolio / Risk / Research  │
└──────────────┬───────────────┘
               │ HTTP API
               ▼
┌──────────────────────────────────────────┐
│           ASP.NET Core Web API           │
│ Portfolio · Transactions · Filings      │
│ Research Runs · Planner · Orchestration  │
└───────┬─────────────┬──────────────┬─────┘
        │             │              │
        ▼             ▼              ▼
 PostgreSQL       Redis Streams     Garage
 / ParadeDB       Queue / State     Object Storage
        │             │
        │             ├───────────────┐
        │             ▼               ▼
        │      Agent Workers   Python Mathematics
        │                      NumPy / SciPy / ARCH
        ▼
 Durable application state

All services → OpenTelemetry → Aspire Dashboard
```

### 執行模型

```text
Client
  │
  ▼
API 建立 Run 與資料庫狀態
  │
  ▼
Reliable enqueue / Redis Streams
  │
  ▼
Worker claim job
  │
  ▼
Planner → Validator → Node Handlers
  │
  ├── 更新 Blackboard
  ├── 寫入 node / tool call 狀態
  ├── 發送 telemetry / events
  └── 完成 immutable Run output
```

背景工作不假設 exactly-once delivery。系統以 `runId`、冪等處理、合法狀態轉換與持久化狀態抵抗重複投遞。

---

## 技術棧

### Backend

- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL 18 / ParadeDB
- pgvector
- StackExchange.Redis
- JWT 與 Google authentication integration
- S3-compatible object storage

### Mathematics

- Python 3.12–3.13
- NumPy
- SciPy
- ARCH
- Pydantic
- redis-py
- pytest

### Frontend

- Vue 3
- Vite
- TypeScript / JavaScript ecosystem

### AI / Agent Runtime

- Microsoft Agents AI
- Google GenAI
- Skill / Capability Registry
- Workflow Planner
- DAG validation
- Blackboard shared state
- Run / Node state machines
- Redis-backed asynchronous execution

### Infrastructure & Observability

- Docker Compose
- Redis 7
- ParadeDB / PostgreSQL
- Garage object storage
- OpenTelemetry
- Aspire Dashboard

---

## Repository 結構

```text
EquityLens/
├── src/
│   ├── EquityLens.Api/          # ASP.NET Core API、資料存取與 Agent runtime
│   └── EquityLens.Mathematics/  # Python 量化風險 worker
├── infra/
│   └── garage/                  # Garage object storage 設定
├── docker-compose.yml           # 本機基礎服務與 mathematics worker
└── README.md
```

實際目錄會隨功能演進；README 只列出主要執行邊界。

---

## 快速開始

### 需求

- Docker Engine 與 Docker Compose
- .NET 10 SDK
- Python 3.12 或 3.13
- Node.js（啟動前端時需要）

### 1. 啟動基礎設施與 Mathematics worker

在 repository 根目錄執行：

```bash
docker compose up -d
```

預設服務：

| Service | 預設連接埠 | 用途 |
|---|---:|---|
| ParadeDB / PostgreSQL | `5432` | 應用程式資料與向量資料 |
| Redis | `6379` | Queue、Streams 與執行狀態 |
| Garage S3 API | `9000` | 財報與文件物件儲存 |
| Garage Web UI | `3909` | 物件儲存管理介面 |
| Aspire Dashboard | `18888` | Logs、traces 與 metrics |
| OTLP gRPC | `4317` | OpenTelemetry ingestion |

檢查服務狀態：

```bash
docker compose ps
docker compose logs -f mathematics
```

只啟動 Redis 與 Mathematics：

```bash
docker compose up -d redis mathematics
```

### 2. 本機直接啟動 Mathematics worker

```bash
cd src/EquityLens.Mathematics
python -m venv .venv
source .venv/bin/activate
pip install -e .

export REDIS_URL=redis://localhost:6379/0
export MATHEMATICS_WORKERS=8
python -m equitylens_mathematics.launcher
```

Windows PowerShell 請將虛擬環境啟用命令改為：

```powershell
.\.venv\Scripts\Activate.ps1
```

### 3. 啟動 API

```bash
cd src/EquityLens.Api
dotnet restore
dotnet run
```

API 還需要依本機環境提供資料庫、Redis、JWT、Google OAuth、S3 與 AI provider 設定。請勿將真實密鑰提交至 Git。

### 4. 停止服務

```bash
docker compose down
```

同時刪除本機 volumes：

```bash
docker compose down -v
```

> [!WARNING]
> `down -v` 會刪除 PostgreSQL、Redis 與 Garage 的本機持久化資料。

---

## 主要資料與工作流概念

### Blackboard

Blackboard 保存 workflow 節點共享的結構化狀態，例如：

- question
- evidence packet
- citations
- calculation inputs / results
- critic findings
- revised answer
- final output

它不是無約束的全域字典。Capability 應宣告所需與產生的 keys，Validator 在執行前檢查 contract。

### Skill、Capability 與 Node

```text
Skill
  └── 授權一組 Capabilities
        └── Capability 對應 Node Type
              └── Planner 建立一或多個 Node Instances
```

Planner 只能從當前 Skill 的允許清單建立 DAG；執行中的 Node 不應任意越權增加未授權能力。

### Run 不可變性

一次 Run 代表一次完整、可稽核的執行紀錄，包括：

- planner proposal 與 DAG
- Blackboard 版本
- Node 與 tool call
- token、成本與耗時
- citations 與最終答案
- 完成狀態與時間

使用者 feedback 不會重新打開已完成 Run，而是在同一 Research Thread / Case 下建立新的 Run。

---

## 測試

### .NET

```bash
dotnet test
```

### Python Mathematics

```bash
cd src/EquityLens.Mathematics
source .venv/bin/activate
pip install -e ".[test]"
pytest
```

測試重點：

- 金融數學與投資組合計算
- Workflow graph 與 capability contract validation
- Run / Node state transitions
- Queue、Worker、Outbox 與 persistence
- LLM structured output schema
- Citation grounding 與研究品質評估

LLM 測試不應只比較完整字串，而應驗證 schema、必要事實、evidence mapping 與品質指標。

---

## 已知限制

- 尚未宣稱具備完整 distributed exactly-once semantics
- 多 worker claim、lease、retry 與 concurrency control 仍持續強化
- Workflow version replay / migration 尚未完整
- VaR backtesting 與模型檢定仍在整合
- 部分壓力測試與 Monte Carlo 功能仍屬研究階段
- AI 研究品質仍需要更完整的 golden dataset 與 evaluation framework
- 開發環境預設值只適用於本機，不應直接沿用至公開部署

---

## Roadmap

### Portfolio & Risk

- [ ] 完整 rolling VaR backtest
- [ ] Breach / exception sequence analysis
- [ ] Kupiec unconditional coverage test
- [ ] Christoffersen independence test
- [ ] 更完整的 stress testing 與模型比較

### Research

- [ ] 強化財報 chunking 與 retrieval
- [ ] Citation-to-claim validation
- [ ] Evidence quality scoring
- [ ] Research evaluation dataset

### Agent Runtime

- [ ] 更完整的 atomic claim、lease 與 retry policy
- [ ] Dead-letter 管理與重放工具
- [ ] Workflow versioning / replay strategy
- [ ] Cost、token 與 latency evaluation dashboard
- [ ] Human approval gates 與副作用治理

### Project Presentation

- [ ] UI screenshots
- [ ] 正式架構圖
- [ ] 可重現 demo dataset
- [ ] End-to-end walkthrough

---

## 設計原則

1. **交易帳務是事實來源，持倉是推導結果。**
2. **金融模型必須揭露假設、資料口徑與限制。**
3. **可由程式驗證的規則，不交給 LLM 猜。**
4. **讓 LLM 處理語義判斷，讓程式控制流程與權限邊界。**
5. **長時間工作與 HTTP request lifecycle 解耦。**
6. **Agent 的中間狀態、失敗、成本與決策必須可觀察。**
7. **Run 是不可變的稽核紀錄，而不是持續變形的對話容器。**
8. **不把 prototype 包裝成 production-ready system。**

---

## 專案定位

> **Portfolio Analytics + Quantitative Risk + Financial Research + Constrained Agentic Workflow**

EquityLens 是一個探索金融資料、量化風險、可靠工作流與 AI orchestration 如何整合的全端工程專案，而不是自動交易機器人或完全自主的投資 Agent。