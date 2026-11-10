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

以自然語言詢問財報、法說會或投資組合問題，由 AI Agent 自主決定如何查資料、算風險、整理答案；
工具權限、數字驗證、副作用審批與結果提交則由程式保證。

> **設計原則：控制邊界與結果，不控制路線。**
> 失敗只會多花一點錢的決策（查什麼、查幾次、何時起草）交給 LLM；
> 失敗會產生錯誤數字、越權或副作用的決策（資料來源權限、數值計算、是否提交答案、是否執行副作用）由確定性程式決定。

#### 三個工作流

```text
Ask Agent / 研究頁 / 投組頁
          │
          ▼
   Router：只決定角色（研究或投組）與參數；入口與追問不經 LLM，
           模糊時由一次 LLM 呼叫分類，參數缺漏、越權或超出能力一律回澄清
          │
   ┌──────┴───────────────────────────────┐
   ▼                                      ▼
ResearchAgent（研究問答、回饋修訂）     PortfolioDiagnosis（投組診斷與優化）
validate → detectIntent                 loadContext
  → researchAgent 🔁                      → portfolioAgent 🔁
  → qualityGate                           → portfolioQualityGate
  → [reviseAgent 🔁 → reviseQualityGate]?  → finalize
  → finalizeResearch

CriticReview（Admin 用，只評分不修改）：loadResearchRun → qualityGate → finalizeCriticReport
```

🔁 = 節點內的受控 Agent 迴圈。外框是固定 DAG（建立 run 時就決定，執行中不再長出節點），
共 3 個工作流、11 種 node type。

#### Agent 迴圈怎麼運作

每一輪：組 prompt → LLM 以 JSON 回傳動作（呼叫工具／提交答案／放棄）→ **先把決策寫入資料庫** →
工具參數與權限檢查 → 同一輪的工具**平行執行**，每個結果先落盤再合併 → 寫 checkpoint → 判斷是否停止。

| 機制 | 做法 |
|---|---|
| 預算 | 每個迴圈最多 6 輪、16 次工具呼叫、1 次網路搜尋、載入 3 個 skill，另有 token 與金額上限 |
| 停止條件 | 提交答案、預算用完、連續無進展、輸出格式連續錯誤；查不到資料就放棄，由系統產生標準拒答 |
| 中斷恢復 | worker 當機後，新 worker 從上一輪 checkpoint 接手，已成功的工具呼叫不重做 |
| 證據壓縮 | 證據完整保存在 ledger，送給 LLM 時只放有長度上限的摘錄 |

#### 工具與 Skill

| 角色 | 工具（全部唯讀） |
|---|---|
| 研究 | `search_local`（本地財報與法說會，Hybrid 檢索＋rerank）、`search_web`（受來源政策限制）、`get_document_page` |
| 投組 | `get_portfolio_snapshot`、`get_risk_report`（優先重用已計算的 Risk Run）、`run_risk_analysis`、`get_performance_attribution`、`simulate_weight_scenario`（減碼轉現金或轉入核准的指數 ETF，例如 0050、006208）、`get_risk_profile` |

- **工具以任務為單位**：數十個底層風險數學函式只在工具內部使用，不直接開放給 LLM；**LLM 不能傳入價格、報酬或權重等數值資料**，數字一律由工具從資料庫讀取。
- **Skill 由 Agent 自行載入**：prompt 只列出 skill 目錄，Agent 需要時呼叫 `load_skill` 載入完整方法（目前有法說會要點、市場事件、投組降波動優化）。Skill 只能增加說明，不能擴大工具權限；每次載入都記錄 id、版本與內容雜湊。
- **來源政策由結構保證**：例如 `LocalOnly` 時 `search_web` 不會出現在工具清單，就算被呼叫也會被拒絕並記錄。

#### 品質關卡與審批

- **Quality Gate（確定性程式）**：檢查引用是否存在、答案中的數字能否追溯到引用來源（處理千分位、仟元／百萬／十億等單位；百分比算式會用來源數字重新計算）、來源角色是否正確（例如不可把年報揭露說成法說會內容）、是否違反來源政策；之後再交給 Critic 評論。不合格就修訂一次，仍不合格則改為標準拒答。
- **投組結論由確定性品質檢查把關**：數字必須能追溯到工具證據、優化建議必須有情境模擬，通過後直接發布。
- **副作用工具審批機制**：審批綁定具體工具呼叫與參數雜湊，核准後重新驗證並以冪等鍵執行（目前正式工具皆為唯讀，此機制以測試工具驗證）。

#### 可靠性與可觀測性

- **Durable runtime**：Redis Streams 佇列、租約 heartbeat 與 fencing（失去租約的 worker 無法寫入）、中斷節點接手、outbox、集中式狀態機
- **完整稽核紀錄**：每一輪 LLM 決策（模型、prompt 版本與雜湊、動作、思考摘要、token、成本、延遲）、每次工具呼叫（含被拒絕者與原因）、事件與 checkpoint 都可查詢
- **OpenTelemetry**：迴圈輪次、LLM 呼叫與工具執行皆有 span，同一輪平行的工具呼叫掛在同一個父 span 下

#### 評估

所有改動以 eval 決定去留，報告保留每一輪原始結果，不挑選最佳答案。

| 評估 | 結果 | 說明 |
|---|---|---|
| 研究問答（台積電、鴻海共 8 題計分） | **7/8** | 與舊引擎 A/B：舊引擎 6/8；輸入 token −46%、估算 LLM 成本 −42%。唯一失敗為「保證獲利」政策題：回答明確表示無法保證，但詞彙評分規則把否定句中的「保證獲利」判為違規（已知問題 KI-004，未修改評分規則） |
| 投組驗收（14 題，同一版本連跑兩輪） | **13/14、14/14** | 含無提示優化、情境比較、資料不足、拒絕保證報酬；持股與交易前後不變。唯一失敗為一次引用標錯來源（數字正確），被 Quality Gate 擋下 |
| 風險與法說會補充題 | 已依原始 PDF 核實 | 獨立計分，不併入原 8 題 |
| Router（28 題） | 題庫與程式評分工具已完成 | 涵蓋模糊、越權、prompt injection、超出能力等情況；真實模型分類尚未執行 |

限制：題庫小且為反覆使用的開發集，單次抽樣，不能推論全面優劣；A/B 當時兩邊皆停用 reranker，與正式設定不同。
自動評分通過不代表敘述完全正確：人工抽查曾發現數字正確但比較方向寫反的回答，因此保留人工檢查。
聯發科與中華電信的財報擷取有已知問題，相關題目以 `excluded_reason` 排除計分。

[Agent Runtime 設計](docs/architecture/research-agent-runtime.md) ·
[三工作流收斂規格](docs/architecture/remove-dynamic-dag.md) ·
[研究 A/B 與成本分析](docs/eval-verification/2026-11-02-agent-loop-ab/README.md) ·
[收斂後驗收](docs/eval-verification/2026-11-03-three-workflows-final-eval/README.md) ·
[投組驗收](docs/eval-verification/2026-11-03-portfolio-acceptance/README.md) ·
[Eval harness](evals/README.md)

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
