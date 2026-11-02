# EquityLens

> 一個整合投資組合分析、量化風險模型、財報資料處理與可控 AI 研究工作流的全端投資研究平台。

EquityLens 將交易紀錄、市場資料、財報與 AI 輔助研究串成一條可追蹤的投資研究流程：

```text
交易與現金流
    ↓
投資組合績效
    ↓
量化風險模型
    ↓
財報與研究資料
    ↓
AI 研究與證據驗證
```

---

## 核心功能

### 投資組合分析

- 以交易紀錄作為帳務事實來源
- 持倉、現金流、股息、成本與損益計算
- 資產配置與投資組合估值
- TWR（時間加權報酬率）
- XIRR（資金加權報酬率）

持倉不是直接儲存的最終狀態，而是由交易與現金流推導而來。

### 量化風險分析

支援多種投資組合風險模型與統計指標：

- Volatility
- Beta
- Sharpe Ratio
- Maximum Drawdown
- VaR / Expected Shortfall
- Correlation / Covariance
- Portfolio Variance
- EWMA
- Covariance Shrinkage
- GBM Monte Carlo
- MVEWMA-FHS

金融數學由獨立 Python worker 執行，並透過 Redis Streams 與 API 非同步協作。

### 財報與研究資料

- 台灣上市櫃財報資料處理
- PDF 下載、保存與文字提取
- S3-compatible object storage
- PostgreSQL / 向量檢索
- 財報資料與 AI Research Workflow 整合

### AI Research Workflow

正式研究流程使用 **固定 DAG＋Agent Loop**。DAG 定義穩定的執行階段，LLM 在工具迴圈內決定查詢內容、使用哪些工具，以及何時提交答案，不再動態產生研究 DAG。

```text
問題
 ↓
請求驗證與意圖辨識
 ↓
Research Agent Loop
 ├─ LLM 選擇搜尋工具與參數
 ├─ 程式檢查來源政策、權限與預算
 └─ 工具結果落盤，依證據補查或起草
 ↓
Output Gate ＋ Evidence Check ＋ Critic
 ├─ 通過 ──────────────────────────────┐
 └─ 未通過 → Revision Agent Loop（最多一次）│
                 ↓                    │
           再次執行完整品質檢查           │
                 ↓                    ↓
             最終答案或標準拒答
```

這是邏輯流程；目前固定 workflow 保留 16 個實體節點，重用既有 Critic handlers。新流程不呼叫動態 Planner，仍檢查持久化圖與節點的一致性。

核心分工：

- LLM 負責工具選擇、查詢調整、回答生成與修訂。
- 程式負責固定流程、工具可見性、參數驗證、來源政策與預算限制。
- Output Gate 檢查引用與數值追溯，Critic 檢查證據是否支持主張。
- Run、Node、LLM Turn、Tool Call 與結果皆可保存與追查。

### 執行可靠性

- 每輪決策與工具結果落盤；worker 接手時重用已保存的結果。
- Heartbeat 與交易式 lease fencing 防止失去租約的舊 worker 提交結果。
- 修訂後再次執行品質檢查，仍不合格就回標準拒答；報酬承諾類問題可回固定風險說明。
- 副作用工具需綁定參數審批與下游冪等鍵。目前正式研究工具均唯讀，副作用路徑以測試工具驗證。

正式環境已啟用 `AgentLoop` 預設。舊 `DynamicDag` 僅保留作相容與回退用途；未設定環境的程式 fallback 仍是舊引擎。本機可設定 `AgentRuntime__ResearchEngine=AgentLoop`，或在 `/api/research/investigations` 請求指定 `engine: "AgentLoop"`。

[設計與驗收](docs/architecture/research-agent-runtime.md) · [實作進度](docs/architecture/research-agent-runtime-progress.md) · [A/B 實測](docs/eval-verification/2026-11-02-agent-loop-ab/README.md) · [面試講稿與示範](docs/architecture/research-agent-runtime-interview.md) · [部署與回退](docs/architecture/research-agent-runtime-deployment.md)

---

## 系統架構

```text
              Vue 3
                │
                ▼
        ASP.NET Core API
          /      |      \
         ▼       ▼       ▼
 PostgreSQL    Redis     S3 Storage
                 │
          ┌──────┴──────┐
          ▼             ▼
   Agent Workers   Python Risk Engine

          OpenTelemetry
                ↓
        Aspire Dashboard
```

---

## 技術亮點

- **交易優先的帳務模型**  
  持倉由交易與現金流推導，而不是直接作為事實來源。

- **獨立量化運算引擎**  
  金融模型由 Python worker 非同步執行，與 Web API 解耦。

- **進階風險模型**  
  除了常見 VaR / ES，也整合 EWMA、Monte Carlo 與 MVEWMA-FHS。

- **受限制的 Agent 架構**  
  固定 DAG 管理執行階段，LLM 在有界工具迴圈內決定研究步驟；程式控制權限、預算與品質檢查。

- **可追蹤的研究流程**  
  Node 執行、證據、Tool Call、成本與最終輸出皆可保存與稽核。

- **完整可觀測性**  
  透過 OpenTelemetry 與 Aspire Dashboard 追蹤 logs、traces 與 metrics。

---

## 技術棧

**Backend**

- .NET 10
- ASP.NET Core
- Entity Framework Core

**Frontend**

- Vue 3
- Vite
- TypeScript

**Data & Infrastructure**

- PostgreSQL / ParadeDB
- Redis
- Docker Compose
- S3-compatible Object Storage

**Quantitative Computing**

- Python
- NumPy
- SciPy
- ARCH

**AI & Observability**

- Microsoft Agents AI
- Google GenAI
- OpenTelemetry
- Aspire Dashboard

---

## Repository 結構

```text
EquityLens/
├── src/
│   ├── EquityLens.Api/
│   └── EquityLens.Mathematics/
├── infra/
├── docker-compose.yml
└── README.md
```

---

## 快速開始

啟動基礎服務：

```bash
docker compose up -d
```

啟動 API：

```bash
AgentRuntime__ResearchEngine=AgentLoop dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj
```

執行測試：

```bash
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj
python -m pytest evals/tests -q
```

實際執行還需要設定 PostgreSQL、Redis、S3、OAuth 與 AI provider 等環境變數。

---

## Roadmap

- VaR Backtesting 與 Kupiec / Christoffersen 檢定
- 強化財報 retrieval 與 citation validation
- Workflow replay 與 worker failure recovery
- Research quality / cost evaluation
- Human approval gates

---

## 專案定位

**Portfolio Analytics + Quantitative Risk + Financial Research + Constrained Agentic Workflow**

EquityLens 探索如何將金融資料、量化模型與可靠的 AI orchestration 整合成一套可追蹤、可驗證的投資研究系統。
