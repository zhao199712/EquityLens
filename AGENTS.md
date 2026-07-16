# EquityLens Agent Guide

## Start Here
- Root `README.md` is a TWSE filing PoC note, not the architecture source; trust executable config and `src/EquityLens.Api/Program.cs` wiring first.
- No `.sln` exists. Run `dotnet` against explicit `.csproj` paths.
- Main backend: `src/EquityLens.Api` (`net10.0`, nullable, implicit usings). Main frontend: `src/EquityLens.Web` (Vue 3 + Vite + TypeScript).
- `Kimi_Agent_*/` directories are standalone Vite prototypes, not the production web app; do not edit them unless explicitly asked.
- Repo-local OpenCode config is `opencode.jsonc`; its Postgres MCP points at port `5433`, while Docker ParadeDB defaults to `5432`.

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
- `dotnet run --project ...` uses `Properties/launchSettings.json`: API on `http://localhost:5034`, OTLP endpoint `http://localhost:4317`, protocol `grpc`. Use `--no-launch-profile --urls ...` to override the port.
- Frontend scripts and lockfile live only under `src/EquityLens.Web`; Vite dev server binds `0.0.0.0` and Vitest uses `jsdom` with `src/**/*.{test,spec}.ts`.
- Backend tests use xUnit, EF InMemory, and hand-written fakes; no Moq/NSubstitute dependency is present.
- Existing `NU1510 System.Text.Encoding.CodePages` warning is non-blocking.

## Infra And Env
- `docker-compose.yml` starts ParadeDB `paradedb/paradedb:0.24.1-pg18`, Redis, Garage S3, Garage WebUI, and Aspire Dashboard.
- Aspire Dashboard is exposed at `${ASPIRE_DASHBOARD_PORT:-18888}`; login token is printed by `docker compose logs aspire-dashboard`. API traces/metrics/logs export through OTLP gRPC at `${OTEL_GRPC_PORT:-4317}` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- `DesignTimeDbContextFactory` reads `ConnectionStrings__PostgreSQL` first, otherwise `POSTGRES_*` env vars with Docker defaults; it does not load `.env` directly.
- `Program.cs` registers `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` because TWSE/MOPS sources use Big5; do not remove it as dead code.
- `src/EquityLens.Api/appsettings.json` contains local live API-key shaped values for DeepSeek/Cohere/Jina/Brave/AlphaVantage; avoid editing or committing it unless explicitly requested.

## Backend Shape
- DI/service registration and CLI import/export modes live in `src/EquityLens.Api/Program.cs`; CLI modes return before normal web startup.
- EF entities/configurations/migrations are in `Data/Entities`, `Data/Configurations`, and `Migrations`.
- Repository classes exist, but newer services often inject `EquityLensDbContext` directly; follow the pattern in the area being edited.
- Controllers use `ICurrentUserContext` for scoped JWT user identity.
- XML doc comments on controllers/services are Traditional Chinese; keep that style when adding public API docs.

## Research Ask Pipeline
- `POST /api/research/ask` persists trace data and returns a `ResearchRunId`; read endpoints are `GET /api/research/runs` and `GET /api/research/runs/{runId}`.
- Local document search calls DB function `search_document_chunks(...)` from migration `20260624164307_AddSearchDocumentChunksFunctionAndBm25Index.cs`; do not replace it with inline LINQ search.
- `search_document_chunks(...)` depends on ParadeDB `pg_search` and `pdb.jieba`; use the compose ParadeDB service, not plain PostgreSQL/Timescale.
- Embeddings use `text-embedding-3-small` with 1536 dimensions in `document_embedding.embedding` (`vector(1536)`).
- Retrieval defaults are in `Services/Ai/RetrievalOptions.cs`: `Hybrid` retrieval and Cohere `rerank-v4.0-fast` when reranking is enabled.
- Preflight checks are skipped for `WebOnly` and `LocalThenWeb` source policies.

## V3 Agent Runtime
- Current V3 runtime is a persisted DAG workflow runner, not Microsoft Agent Framework runtime; keep MAF behind seams such as `ICriticReviewAgent` / `IDraftRevisionAgent`.
- Workflow definitions are persisted in `AgentRun.WorkflowDefinitionJson`; `AgentWorkflowPlanner` topologically sorts `nodes`/`edges`; `AgentRunGraphValidator` checks persisted `AgentRun.Nodes` match the definition.
- Run/node status transitions are centralized in `AgentStateMachine.cs`; use `Transition(...)` / `ResetForRetry(...)`, not direct `Status = ...` assignments.
- Agent telemetry is in `EquityLensTelemetry`: spans `agent.run.execute` / `agent.node.execute`, metrics `equitylens.agent.*.status.transitions`; persisted `events`, `nodes`, and `toolCalls` remain the source of truth.
- Node handlers in `CriticReviewNodeHandlers.cs` and `DraftRevisionNodeHandlers.cs` should only execute node business logic and write `InputJson`, `OutputJson`, `BlackboardJson`, tool calls, and events.
- Current workflows:
```text
CriticReview: loadResearchRun -> buildEvidencePacket -> checkEvidence -> critiqueAnswer -> finalizeCriticReport
DraftRevision: loadCriticReviewRun -> draftRevisedAnswer -> finalizeRevision
```
- Add a workflow by adding constants, provider, node contracts/blackboard keys, handlers, DI registrations, and tests for definition contract, planner order, handler coverage, blackboard/output, failure/retry, and user isolation.
- Production DI maps `ICriticReviewAgent` to `LlmCriticReviewAgent` and `IDraftRevisionAgent` to `LlmDraftRevisionAgent`; tests use deterministic agents/fakes.
- DeepSeek JSON mode is `ChatResponseFormat.JsonObject`; Gemini currently throws `NotSupportedException` for that response format.

## Data Pipeline CLI Modes
```bash
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-conferences
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --chunk-conferences
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --embed-chunks
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-finmind-financials [--from YYYY-MM-DD] [--to YYYY-MM-DD]
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-mops-financials [--from YYYY-MM-DD] [--to YYYY-MM-DD]
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --download-twse-reports [--output ./exports/financial-reports]
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-twse-report-files
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --export-embeddings [path]
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --export-financials-csv [path]
```

## Do Not Commit
- `annual-report-chunks*.jsonl`, `exports/`, `0050_prices.csv`, `mops_response_full.html`, benchmark output under `exports/benchmark/`.
- `法說會/`, `.playwright-mcp/`, downloaded PDF originals, or local session scratch files.
- Secret or local-key changes in `src/EquityLens.Api/appsettings.json`.
