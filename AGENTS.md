# EquityLens Agent Guide

## Start Here
- Root `README.md` is the project overview and portfolio-facing entry point. For implementation details, trust executable config, the organized `docs/` tree, and `src/EquityLens.Api/Program.cs` wiring.
- No `.sln` exists. Run `dotnet` against explicit `.csproj` paths.
- Main backend: `src/EquityLens.Api` (`net10.0`, nullable, implicit usings). Main frontend: `src/EquityLens.Web` (Vue 3 + Vite + TypeScript).
- `prototypes/` contains standalone historical UI experiments, not the production web app; do not edit them unless explicitly asked.
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
npm run test:e2e:ci # production frontend + mocked API, no paid AI or live backend

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
- PR CI runs real ParadeDB integration tests and fails if TRX results are missing or skipped. Browser smoke uses isolated fixed API responses; it does not validate live AI quality or full-stack HTTP behavior. Setup and scope: `docs/testing/pr-validation.md`.
- Throwaway ParadeDB and Redis for integration tests (cloud or local dev containers): `scripts/dev/test-services.sh up`, then `eval "$(scripts/dev/test-services.sh env)"` to set the backend and risk integration variables, and `down` to remove them. It starts `dockerd` if needed and pulls through `mirror.gcr.io` because anonymous Docker Hub pulls from shared cloud IPs often return 429. Run the risk suite with the Mathematics venv first on `PATH`. Never point these variables at production.
- Risk contracts and the real DB → C# → Redis → Python → DB chain run separately in `tests/EquityLens.Risk.IntegrationTests`. Install the Mathematics Python package and set `RISK_TEST_POSTGRES`, `RISK_TEST_REDIS`, and `RISK_TEST_REDIS_URL` to isolated services; missing dependencies fail rather than skip. The suite starts its own Python worker and C# result worker; do not point it at production services. See `docs/testing/pr-validation.md` for commands.

## Infra And Env
- Manual deployment entrypoint: `scripts/deploy/deploy.sh` (plan-only unless `--execute`). It requires an exact green main SHA and explicit live Compose/env paths; see `docs/operations/manual-deployment.md`. PR CI only tests its guards. The manual main-only deploy workflow is the sole runner entrypoint; see `docs/operations/github-manual-deployment.md`. Never dispatch production jobs from PR CI. It does not migrate or restore databases.
- `docker-compose.yml` starts ParadeDB `paradedb/paradedb:0.24.1-pg18`, Redis, Garage S3, Garage WebUI, and Aspire Dashboard.
- Aspire Dashboard is exposed at `${ASPIRE_DASHBOARD_PORT:-18888}`; login token is printed by `docker compose logs aspire-dashboard`. API traces/metrics/logs export through OTLP gRPC at `${OTEL_GRPC_PORT:-4317}` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- `DesignTimeDbContextFactory` reads `ConnectionStrings__PostgreSQL` first, otherwise `POSTGRES_*` env vars with Docker defaults; it does not load `.env` directly.
- `Program.cs` registers `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` because TWSE/MOPS sources use Big5; do not remove it as dead code.
- `src/EquityLens.Api/appsettings.json` contains local live API-key shaped values for DeepSeek/Cohere/Jina/Brave/AlphaVantage; avoid editing or committing it unless explicitly requested.

## Frontend Shape
- Visual systems: legacy "kimi" editorial (`src/assets/kimi-design.css`, `src/components/kimi/`), "tech" neon (`src/assets/tech-design.css`, `src/components/tech/`), and the current production system "prestige" (`src/assets/prestige-design.css`, midnight navy + champagne gold, classes `.prestige-page`/`.prestige-panel`/`.prestige-btn`/`.prestige-input`/`.prestige-table`, tokens scoped under `.prestige-page`). New pages should use the prestige system.
- Do not add a bare `page` class to view roots: `src/style.css` has a global `.page { width: min(1280px,100%); margin: 0 auto }` that will silently constrain full-bleed pages.
- ECharts is registered exactly once in `src/components/tech/TechChart.vue` (`vue-echarts` wrapper); reuse it for new charts instead of calling `echarts/core` `use()` elsewhere. The kimi chart components are pure SVG and take color props.
- `/` is the public Prestige landing (`PrestigeHomeView`, mock data in `src/data/homeTechData.ts`, `meta.requiresAuth: false`). Dashboard and static financial Reports pages were removed; `/dashboard` redirects to `/portfolios`, and `/financial-reports/:id?` redirects to `/research`. Ordinary login/register flows land on portfolios. Style-lab variants under `/home/*` (`/home/lab` is the index) are registered only in development builds; unknown production URLs render the public 404 page.
- The `equity-lines` feature was removed (route, nav, locales, `src/views/equity-lines/`, `src/services/equityLines.ts`); do not reintroduce imports of it.

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

## Fixed Workflow Runtime
- Persisted DAG runtime with exactly three workflows and 13 node types; node-level research and portfolio loops share `Loop/AgentLoopEngine.cs`. This is not Microsoft Agent Framework runtime.
- `AgentWorkflowPlanner` topologically sorts fixed nodes/edges; `AgentRunGraphValidator` checks persisted nodes. No runtime graph expansion, capability registry, or supporting-skill catalog remains.
- Definitions are persisted in `AgentRun.WorkflowDefinitionJson`. `WorkflowExecutionCompatibility` checks workflow, version and both definition/persisted node shapes. Retired runs remain readable; retry/resume/approval return 409 after ownership validation. Workers acquire fenced leases before safely failing retired graphs.
- Current workflow definitions:
```text
ResearchAgent v2: validate -> detectIntent -> researchAgent -> qualityGate -> [reviseAgent -> reviseQualityGate] -> finalize
PortfolioDiagnosis v4: loadContext -> portfolioAgent -> portfolioQualityGate -> approve -> finalize / finalizeRejected
CriticReview v2: loadResearchRun -> qualityGate (score only) -> finalizeReport
```
- Research and revision share a loop type; both gates share QualityGate. Research revises at most once. Feedback creates a ResearchAgent child beginning at revision, preserving request deduplication and parent links.
- QualityGate stages persist their results and reuse a completed Critic on recovery. Stages are ordinary components, not registered nodes. Score mode does not rewrite the original answer.
- Node registration lives in Bootstrap wiring and `AgentWorkflowCatalog.cs`; maintain their exact correspondence. Current catalog counts have regression tests.
- Use `AgentStateMachine.Transition(...)` / `ResetForRetry(...)`, not direct status assignments. Handler business logic writes input/output, blackboard, tool calls and events; executor owns transitions and leases.
- Telemetry uses `agent.run.execute` / `agent.node.execute` and `equitylens.agent.*.status.transitions`; persisted events/nodes/toolCalls remain the source of truth.
- `WaitForHumanApproval` is always on for portfolio output. It durably suspends, releases the lease and writes no wake outbox. External approval restores the gate and follows conditional edges. Rejection requires a reason and ends Failed; no holdings are modified.
- Conditional edges resolve dotted blackboard paths. Approval uses the existing ownership, orchestration concurrency and requestId guards.
- Ask Agent uses one unified conversation router: action, workflow, params, missing, confidence. Explicit entry points and owned-run follow-ups bypass classification. All paths validate ownership, ambiguity, source policy and supported scenarios. Invalid routing JSON retries once, then clarifies with sanitized diagnostics.
- Router never selects a method. Role-scoped method files under `AgentSkills/` are loaded by audited read-only `load_skill` (default maximum three). They can narrow tools but cannot expand authorization or source policy.

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

## Research Runtime reliability
- Executor returns an outcome; the worker must not acknowledge `LeaseHeldByOther` or `LeaseLostDuringExecution`.
- `LeaseVersion` changes on claim. Executor-scoped `AgentRunWriteFence` protects every async DbContext save with a PostgreSQL lease row lock in the same transaction as results. Synchronous saves during execution are rejected.
- Heartbeat uses an independent scope/DbContext. Never share a handler DbContext with background renewal.
- Interrupted Ready/Queued/Running nodes recover through the state machine with a bounded interruption count; caller cancellation and lease loss must not publish stale failure results.

## Shared Agent Loop
- Investigation defaults to AgentLoop; explicit DynamicDag returns 400. Eval live CLI accepts only AgentLoop; saved legacy responses remain rescorable.
- Persist each LLM decision before dispatch and each tool result before merging evidence/domain state. Checkpoints are scoped to node + execution attempt; committed results are reused after lease recovery.
- Research tools: search_local, search_web (source-policy constrained, at most once), get_document_page. Portfolio has six task tools: snapshot, saved Risk Run, enum-selected risk analysis, attribution, scenario simulation and risk profile. The 37 internal math functions are not exposed to the model. Scenario policy `index_etf` can transfer reductions into approved TWD/TWSE index ETFs (0050/006208), including nonholdings. Snapshot candidates require matching ETF metadata and complete adjusted prices for every frozen holding date (all holdings must also use adjusted prices); missing data stays unavailable. New assets have zero baseline quantity, never become DB holdings, and cannot be reduction sources. Cash, holding and ETF scenarios share the same dates/baseline; HHI is security-level, not ETF constituent look-through.
- Tool arguments cannot supply price/return arrays or calculated financial results. Values come from owner-scoped DB/blackboard. Current tools are read-only. Any future side-effect tool requires durable approval, argument binding and downstream idempotency.
- OutputGate is deterministic; revision invokes Critic again. Final refusal records quality_gate_rejected and blocked numbers; successful execution does not imply an accepted answer.
- Preserve unknown trajectory indicators for old eval responses rather than inventing zero violations. Only verified, non-excluded original cases contribute to the original eight-case score; new corpus cases are reported separately.
