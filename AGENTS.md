# EquityLens Agent Guide

## Start Here
- Root README is a TWSE filing PoC note, not a full architecture guide; trust project files and `Program.cs` wiring over README prose.
- No `.sln` exists; run `dotnet` commands against explicit `.csproj` paths.
- Main backend: `src/EquityLens.Api` (`net10.0`, nullable, implicit usings). Main frontend: `src/EquityLens.Web` (Vue 3 + Vite + TypeScript).
- `Kimi_Agent_*/` are standalone Vite prototypes, not the production web app; do not edit them unless explicitly asked.
- Repo-local OpenCode config exists at `opencode.jsonc`; its postgres MCP uses port `5433`, while Docker ParadeDB defaults to `5432`.

## Commands
```bash
# Backend
dotnet build src/EquityLens.Api/EquityLens.Api.csproj
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj
dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj --filter FullyQualifiedName~AgentStateMachineTests
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj
dotnet run --no-launch-profile --project src/EquityLens.Api/EquityLens.Api.csproj --urls http://localhost:5035

# Frontend, from src/EquityLens.Web
npm install
npm run dev
npm run build
npm test

# Local services
docker compose up -d

# EF migrations
~/.dotnet/tools/dotnet-ef database update --project src/EquityLens.Api --startup-project src/EquityLens.Api
ConnectionStrings__PostgreSQL="Host=localhost:5432;Database=equitylens;Username=equitylens;Password=equitylens_dev_password" ~/.dotnet/tools/dotnet-ef migrations add <Name> --project src/EquityLens.Api --startup-project src/EquityLens.Api
~/.dotnet/tools/dotnet-ef migrations has-pending-model-changes --project src/EquityLens.Api --startup-project src/EquityLens.Api
```
- `dotnet run --project ...` uses `Properties/launchSettings.json` and serves `http://localhost:5034`; use `--no-launch-profile --urls ...` to override the port.
- Frontend Vite dev server binds `0.0.0.0`; package scripts are only in `src/EquityLens.Web/package.json`.
- Backend test suite is xUnit with EF InMemory and hand-written fakes; no Moq/NSubstitute dependency is present.
- Expect existing warning `NU1510 System.Text.Encoding.CodePages`; it is currently non-blocking.

## Infra And Env Gotchas
- `docker-compose.yml` starts ParadeDB `paradedb/paradedb:0.24.1-pg18` on `${POSTGRES_PORT:-5432}`, Redis, Garage S3, Garage WebUI, and Aspire dashboard.
- `DesignTimeDbContextFactory` reads `ConnectionStrings__PostgreSQL` first, otherwise `POSTGRES_*` env vars with Docker defaults; it does not read `.env` directly.
- `Program.cs` registers `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` because TWSE/MOPS sources use Big5; do not remove it as dead code.
- `appsettings.json` contains local live API-key shaped values for DeepSeek/Cohere/Jina/Brave/AlphaVantage; avoid editing or committing it unless explicitly requested.

## Backend Shape
- DI/service registration lives in `src/EquityLens.Api/Program.cs`; CLI import modes are also handled there before normal web startup.
- EF entities/configurations/migrations are in `Data/Entities`, `Data/Configurations`, and `Migrations`.
- Repository classes exist, but newer services often inject `EquityLensDbContext` directly; follow the pattern in the area being edited.
- Controllers use `ICurrentUserContext` for scoped JWT user identity.
- XML doc comments on controllers/services are Traditional Chinese; keep that style when adding public API docs.

## Research Ask Pipeline
- `POST /api/research/ask` persists trace data and returns a `ResearchRunId`; read endpoints are `GET /api/research/runs` and `GET /api/research/runs/{runId}`.
- Local document search calls DB function `search_document_chunks(...)` from migration `20260624164307_AddSearchDocumentChunksFunctionAndBm25Index.cs`; do not replace it with inline LINQ search.
- That function depends on ParadeDB `pg_search` and `pdb.jieba`; use the ParadeDB compose service, not a plain PostgreSQL/Timescale sidecar.
- Embeddings use `text-embedding-3-small` with 1536 dimensions in `document_embedding.embedding` (`vector(1536)`).
- Retrieval defaults are in `Services/Ai/RetrievalOptions.cs`: `Hybrid` retrieval and Cohere `rerank-v4.0-fast` when reranking is enabled.
- Preflight checks are skipped for `WebOnly` and `LocalThenWeb` source policies.

## V3 Agent Runtime
- Current V3 runtime is a persisted DAG workflow runner, not Microsoft Agent Framework runtime; `Microsoft.Agents.AI` is referenced but MAF should stay behind seams such as `ICriticReviewAgent` / `IDraftRevisionAgent`.
- DAG definitions are produced by `AgentWorkflowDefinitionProvider` into `AgentRun.WorkflowDefinitionJson`; `AgentWorkflowPlanner` topologically sorts `nodes`/`edges`; `AgentRunGraphValidator` ensures persisted `AgentRun.Nodes` match the definition.
- Run/node status transitions are centralized in `AgentStateMachine.cs`; `AgentRunService` must use state-machine `Transition`/`ResetForRetry`, not direct `Status = ...` assignments.
- Node handlers in `CriticReviewNodeHandlers.cs` and `DraftRevisionNodeHandlers.cs` should only execute node business logic and write `InputJson`, `OutputJson`, `BlackboardJson`, tool calls, and events.
- Current workflows:
```text
CriticReview: loadResearchRun -> buildEvidencePacket -> checkEvidence -> critiqueAnswer -> finalizeCriticReport
DraftRevision: loadCriticReviewRun -> draftRevisedAnswer -> finalizeRevision
```
- Add a workflow by adding constants, provider, node contracts, blackboard keys if needed, handlers, DI registrations, and tests for definition contract, planner order, handler coverage, blackboard/output, failure/retry, and user isolation.
- Production DI maps `ICriticReviewAgent` to `LlmCriticReviewAgent` and `IDraftRevisionAgent` to `LlmDraftRevisionAgent`; tests use deterministic agents/fakes.
- DeepSeek JSON mode is represented by `ChatResponseFormat.JsonObject`; Gemini currently throws `NotSupportedException` for that response format.

## Data Pipeline CLI Modes
```bash
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-conferences
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --chunk-conferences
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --embed-chunks
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-finmind-financials [--from 2023-01-01] [--to 2026-07-07]
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-mops-financials [--from 2023-01-01] [--to 2026-07-07]
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --download-twse-reports [--output ./exports/financial-reports]
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-twse-report-files
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --export-embeddings [path]
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --export-financials-csv [path]
```

## Do Not Commit
- `annual-report-chunks*.jsonl`, `exports/`, `0050_prices.csv`, `mops_response_full.html`, benchmark output under `exports/benchmark/`.
- `法說會/`, `.playwright-mcp/`, downloaded PDF originals, or local session scratch files.
- Secret or local-key changes in `src/EquityLens.Api/appsettings.json`.
