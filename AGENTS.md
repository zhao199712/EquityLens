# EquityLens Agent Guide

## Environment
- Linux bash, repo root assumed unless noted.
- No README, opencode.json, or .cursorrules; single canonical instruction file.

## Repo Shape
- Backend API: `src/EquityLens.Api` (net10.0, nullable enabled, implicit usings). DI in `Program.cs:540` lines.
- Frontend: `src/EquityLens.Web` (Vue 3 + Vite + TypeScript). Entrypoints: `src/main.ts`, `src/App.vue`, `src/router/index.ts`.
- Three separate Vite prototypes in `Kimi_Agent_*/` — not the main app, do not touch.
- Infra: `docker compose up -d` — ParadeDB (pg18, port 5432), Redis, Garage S3, and Garage WebUI.
- Data-pipeline scripts: `scripts/` has shell/Python/.NET helpers for annual-report ingestion.
- No integration tests; all tests are xUnit unit tests with hand-written fakes (no Moq/NSubstitute).

## Commands
```bash
# Backend
dotnet build src/EquityLens.Api/EquityLens.Api.csproj
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj      # xUnit, 214 tests, no db required
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj           # http://localhost:5034

# Override port (launch settings ignored by --no-launch-profile)
dotnet run --no-launch-profile --project src/EquityLens.Api/EquityLens.Api.csproj --urls http://localhost:5035

# Frontend (from src/EquityLens.Web)
npm install && npm run dev       # http://localhost:5173
npm run build                    # vue-tsc -b && vite build

# EF migrations
alias ef=~/.dotnet/tools/dotnet-ef
ef database update --project src/EquityLens.Api --startup-project src/EquityLens.Api
ConnectionStrings__PostgreSQL="Host=localhost:5432;Database=equitylens;Username=ymsh20220;Password=a19971105" ef migrations add <Name> --project src/EquityLens.Api --startup-project src/EquityLens.Api
```
- `dotnet run` uses `launchSettings.json` (port 5034). `ASPNETCORE_URLS` env var is **ignored**; use `--urls` with `--no-launch-profile`.
- Design-time connection in `Data/DesignTimeDbContextFactory.cs` reads `ConnectionStrings__PostgreSQL` env var, not `.env`.

## Backend Architecture
- EF entities in `Data/Entities`, fluent configs in `Data/Configurations`, migrations in `Migrations/`.
- Repository pattern exists (14 repos in `Repositories/`) but newer services inject `EquityLensDbContext` directly; prefer the pattern closest to the code you're editing.
- Controllers inject `ICurrentUserContext` (scoped, reads JWT claims) for user identity.
- `Program.cs` registers `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` — required because TWSE/MOPS sources use Big5.
- `appsettings.json` has live local API keys (DeepSeek, Cohere, Jina, Brave, AlphaVantage); do not commit changes to it.
- All tests use hand-written fakes, not mocking libraries.

## Document Search / `/api/research/ask`
- Underlying search calls a **ParadeDB function** `search_document_chunks(...)` (not inline LINQ) defined in migration `20260624164307_*`. Requires `pg_search` extension + `pdb.jieba` type (ParadeDB only, not the TimescaleDB sidecar).
- Embeddings: `text-embedding-3-small`, 1536 dims, stored in `document_embedding.embedding` (`vector(1536)`).
- Retrieval mode: `appsettings.json:Retrieval:RetrievalMode` = `"Hybrid"` (Jieba BM25 + vector with RRF); also `"VectorOnly"` / `"Bm25Only"`.
- External reranker: Cohere `rerank-v4.0-fast` (config via `Retrieval:RerankProvider`).

### `POST /api/research/ask`
| Param | Type | Notes |
|---|---|---|
| `ticker` | string | Required. Must be in 0050 universe. |
| `question` | string | Required. |
| `retrievalMode` | enum | `Auto`(default), `ConferenceOnly`, `AnnualReportOnly`, `AllDocuments` |
| `documentType` | string | `AnnualReport` / `EarningsPresentation` / `auto`(null == ask planner) |
| `sourcePolicy` | enum | `LocalOnly`(default), `LocalThenWeb`, `LocalAndWeb`, `WebOnly` |
| `topK` | int | default 8, clamped [1, 20] |
| `temperature` | double | default 0.2, clamped [0, 1] |
| `debug` | bool | If true, response includes `Trace` with full retrieval decisions. |

Response includes `ResearchRunId` (persisted trace record), `Status` (`Answered` / `InsufficientEvidence` / `CitationValidationFailed`), `Citations[]` with sentence-level `QuoteText`.

Preflight checks (ticker exists → 0050 member → has documents/chunks/embeddings) are skipped for `WebOnly` and `LocalThenWeb`.

## Trace Persistence (added 2026-06)
Every `/ask` call persists to these DB tables (fail-open — write failure does not break the response):
- `research_run` — top-level run metadata.
- `research_run_step` — pipeline steps (`IntentDetection`, `RetrievalPlanning`, `LocalRetrieval`, `WebRetrieval`, `Rerank`, `AnswerGeneration`), each with input/output JSON, timing, error.
- `research_run_candidate` — every candidate chunk's fate: `Decision` (`Selected`, `DiscardedByTopK`, `DiscardedByRiskEvidenceFilter`, etc.) + scores + rank.
- `research_run_citation` — final answer citations with `CitationIndex`, `QuoteText`, `SourceType`.

### Read endpoints
- `GET /api/research/runs?limit=50&ticker=2330&status=Answered`
- `GET /api/research/runs/{runId}` — returns run + steps + candidates + citations.

## V3 Agent Platform Skeleton
Current V3 agent runtime is a persisted DAG workflow skeleton, not MAF yet. Keep MAF deferred behind interfaces such as `ICriticReviewAgent` until the product runtime is stable.

Core runtime files:
- `Services/Agents/AgentRunService.cs` — creates/list/gets/retries/cancels runs, owns run/node status transitions and timeline events.
- `Services/Agents/AgentWorkflowDefinitionProvider.cs` — workflow providers create initial `AgentRun`, `AgentRunNode` list, `WorkflowDefinitionJson`, and initial `BlackboardJson`.
- `Services/Agents/AgentWorkflowPlanner.cs` — reads `WorkflowDefinitionJson` `nodes`/`edges` and topologically sorts execution order.
- `Services/Agents/AgentRunGraphValidator.cs` — checks planned node keys match persisted `AgentRun.Nodes` exactly.
- `Services/Agents/*NodeHandlers.cs` — node handlers execute one node each.
- `Services/Agents/AgentBlackboardContracts.cs` and `AgentNodeContracts.cs` — shared JSON contract fields and node input/output record shapes.

Runtime concepts:
- Workflow = task flow definition (`workflowType`, `version`, `nodes`, `edges`).
- Provider = creates the workflow definition, initial nodes, and blackboard.
- Planner = derives execution order from DAG edges, not hardcoded node order.
- Validator = verifies workflow definition and persisted nodes are consistent before execution.
- Handler = executes a single node and writes node output / blackboard updates.
- BlackboardJson = shared state passed between nodes; OutputJson = final workflow output.

Current workflows:
```text
CriticReview:
loadResearchRun -> checkEvidence -> critiqueAnswer -> finalizeCriticReport

DraftRevision:
loadCriticReviewRun -> draftRevisedAnswer -> finalizeRevision
```

Current execution path:
```text
AgentRunsController
 -> AgentRunService
    -> workflow provider registry
    -> AgentWorkflowPlanner
    -> AgentRunGraphValidator
    -> IAgentNodeHandler.ExecuteAsync(...)
    -> BlackboardJson / OutputJson / AgentRunEvent / AgentToolCall persistence
```

Extension rules:
- Add a workflow by adding constants, a provider, node contracts, blackboard fields if needed, handlers, DI registrations, and tests.
- Register providers as `IAgentWorkflowDefinitionProvider`; `AgentRunService` selects them by `WorkflowType`.
- Register each node handler as `IAgentNodeHandler`; missing handlers intentionally fail the run.
- Do not hardcode execution order in `AgentRunService`; put DAG edges in `WorkflowDefinitionJson` and let `AgentWorkflowPlanner` sort.
- Keep deterministic agent implementations for tests; add LLM/MAF implementations behind existing interfaces.
- Test workflow definition contract, planner order, handler coverage, blackboard/output contract, failure/retry behavior, and user isolation.

## Coding Conventions
- XML doc comments on controllers/services are in Traditional Chinese; keep that style.
- Namespace folder layout matches directory structure (`Services/Ai/`, `Services/Research/`, etc.).
- New research ask trace tables follow snake_case column naming; `gen_random_uuid()` for PK defaults.
- Hand-written fakes in test files (no mocking framework). Add new fakes at file bottom near other sealed fakes.

## Data Ingestion (CLI modes)
```
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-finmind-financials [--from 2023-01-01] [--to 2025-12-31]
dotnet run -- ... --download-twse-reports [--output ./exports/financial-reports]
dotnet run -- ... --import-twse-report-files
dotnet run -- ... --embed-chunks
dotnet run -- ... --import-conferences
dotnet run -- ... --chunk-conferences
```

## Do Not Commit
- `annual-report-chunks*.jsonl`, `exports/`, `0050_prices.csv`, `mops_response_full.html`
- `法說會/`, `.playwright-mcp/`, PDF originals, benchmark output in `exports/benchmark/`
- Changes to `appsettings.json` (live local API keys)
