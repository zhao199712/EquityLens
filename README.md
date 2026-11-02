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

```text
問題
 ↓
Router
 ↓
Planner
 ↓
DAG / Contract Validation
 ↓
Workers
 ↓
Evidence Check
 ↓
Critique
 ↓
Revision
 ↓
Final Answer
```

EquityLens 不採用完全自由的 Autonomous Agent，而是以受限制的能力、可驗證的流程與可追蹤的執行紀錄控制 AI 行為。

核心原則：

- LLM 負責語義理解、規劃與生成
- 程式負責權限、流程與 deterministic validation
- Capability 必須受到 Skill 授權範圍限制
- Run、Node、Tool Call、成本與結果皆可追蹤

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
  LLM 負責規劃與推理，程式則負責權限與執行邊界。

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
cd src/EquityLens.Api
dotnet run
```

執行測試：

```bash
dotnet test
pytest
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
