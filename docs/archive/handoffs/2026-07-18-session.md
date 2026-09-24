# EquityLens Session Handoff — 2026-07-18

## Repository and branch

- Repository: `EquityLens`
- Active branch: `backend`
- Latest remote commit: `2314db5 refactor: centralize agent node contracts`

## Completed work

### Risk performance and observability

- Added Risk API OpenTelemetry spans, metrics and correlated logs for Aspire.
- Deferred Risk detail page heavy requests; backtest and Monte Carlo load on scroll.
- Added persisted background Risk Backtest Run support and reduced repeated 95%/99% simulation work.

### Workflow and Admin management

- Existing Admin Workflow / Node Catalog management is deployed.
- Admin can manage enable state, display values, timeout and retry policy; each Run snapshots execution policy.

### PortfolioDiagnosis workflow

- Added `PortfolioDiagnosis` fixed workflow:
  `LoadPortfolioDiagnosisContext` → `CalculatePerformanceAttribution` → `LoadRiskProfile` → `PrioritizeRiskAnalyses` → `BuildPortfolioEvidencePacket` → `DraftPortfolioDiagnosis` → `FinalizePortfolioDiagnosis`.
- Added `POST /api/portfolios/{portfolioId}/agent-diagnoses` and portfolio-page entry point.
- Final output contains relative performance, approximate holding/industry contribution, evidence coverage and recommended risk analyses.
- Agent Run detail page renders an `AI 投組診斷報告` card.
- Fixed background worker ownership lookup and UTC price-query boundaries.

### Node Contract single source of truth

- `AgentWorkflowCatalog` now owns immutable `AgentNodeContract` definitions.
- Contract includes version, stage, side effect, input/output schema name, blackboard keys, allowed next nodes, default policy and execution flags.
- `NodeMetadata.cs` is now derived from Catalog rather than a second hand-maintained source.
- New Runs snapshot contract plus effective execution policy in `WorkflowDefinitionJson`.
- Admin Node Catalog displays readonly Contract; `metadata_json` is now explicitly optional Admin supplemental metadata.

## Verification

- API build passes (only existing `NU1510` warning).
- Frontend build passes (only existing Rolldown annotation / bundle-size warnings).
- Agent workflow / graph tests: 18 passed.

## Remote deployment history

- Previous remote host used: `ymsh20220@192.168.50.11`.
- Previous remote Docker services: Postgres, Redis, Garage and Aspire Dashboard through `docker compose`.
- API was manually started at `127.0.0.1:5034`; later stopped so VS Code F5 can own the port.
- JWT key is process-scoped development configuration; restarting manually requires a new key and invalidates login tokens.

## Important operating notes

- Do not run a manual API and VS Code F5 on port 5034 at the same time.
- Before starting API on a new remote, inspect the port owner first: `ss -ltnp '( sport = :5034 )'`.
- Vite normally uses port 5173; inspect before starting it similarly.
- The current new remote host/address has not yet been supplied in this session.
