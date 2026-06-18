# EquityLens Agent Guide

## Environment Assumptions
- Linux shell (bash). All commands assume repo root unless otherwise noted.

## Repo Shape
- Backend API: `src/EquityLens.Api` (`net10.0`, nullable enabled, implicit usings). Entrypoint and DI wiring in `Program.cs`.
- Frontend: `src/EquityLens.Web` (Vue 3 + Vite + TypeScript). Entrypoints: `src/main.ts`, `src/App.vue`, `src/router/index.ts`.
- Prototype: `Kimi_Agent_科技感投資前端/` and `Kimi_Agent_资产组合压力测试Vue v2/` are separate Vite prototypes, not the main app.
- Data-pipeline scripts: `scripts/` contains shell/Python/.NET helpers and SQL migration scripts used by the annual-report / financial-statement ingestion flow.
- Local infra: `docker-compose.yml` runs PostgreSQL/TimescaleDB, Redis, Garage S3 API, and Garage Web UI.

## Commands
- Start infra: `docker compose up -d`
- Backend build/test/run:
  - `dotnet build src/EquityLens.Api/EquityLens.Api.csproj`
  - `dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj`
  - `dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj`
- API dev URL: `http://localhost:5034`; Swagger at `/swagger` only in Development.
- Frontend (from `src/EquityLens.Web`):
  - `npm install`
  - `npm run dev`
  - `npm run build` (runs `vue-tsc -b && vite build`)
- EF migrations:
  - `dotnet-ef` is not built-in; install with `dotnet tool install --global dotnet-ef`.
  - `dotnet ef database update --project src/EquityLens.Api/EquityLens.Api.csproj --startup-project src/EquityLens.Api/EquityLens.Api.csproj`
  - Design-time connection is hardcoded in `Data/DesignTimeDbContextFactory.cs`; if migrations fail on connection, check that file first.

## Backend Conventions
- EF entities live in `Data/Entities`, explicit mappings in `Data/Configurations`, migrations in `src/EquityLens.Api/Migrations`. Apply migrations before testing new columns.
- Repository/service/controller patterns are established; prefer extending existing repositories/services over adding cross-cutting abstractions.
- XML doc comments in controllers/services are in Traditional Chinese; keep that style for new public API comments.
- `Program.cs` registers `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` at the top because TWSE/MOPS sources use Big5 on Linux.
- Do not edit `bin`, `obj`, build output, or generated EF snapshot content except through migrations.

## Data Ingestion Pipeline
`Program.cs` supports several CLI-only modes. Each runs the DI container, executes the operation, and exits.

```bash
# Structured financial statements (FinMind is the reliable source)
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-finmind-financials [--from 2023-01-01] [--to 2025-12-31]

# Download TWSE annual-report PDFs to exports/financial-reports
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --download-twse-reports [--output ./exports/financial-reports]

# Upload downloaded TWSE PDFs to Garage + create DB metadata
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-twse-report-files

# Embed any chunks missing embeddings (idempotent; skips already-embedded chunks)
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --embed-chunks

# Export embeddings JSONL or structured financials CSV
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --export-embeddings ./exports/embeddings.jsonl
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --export-financials-csv ./exports/financials.csv

# Investor-conference pipeline
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --import-conferences
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj -- --chunk-conferences
```

- `scripts/AnnualReportExtractor/Program.cs` is the .NET harness for extracting text from annual-report PDFs; Python scripts in `scripts/` handle filtering and DB insertion of the resulting chunks.
- Embeddings use `text-embedding-3-small`, 1536 dimensions, stored in `document_embedding.embedding` (`vector(1536)`).

## Frontend Gotchas
- Axios default `baseURL` is `http://localhost:8080`; backend dev server runs on `http://localhost:5034`. Set `VITE_API_BASE_URL` or configure a proxy.
- Backend CORS only allows `http://localhost:5173` (Vite dev server).
- Vue router `beforeEach` auth guard is commented out; routes do not enforce authentication.
- Demo user id is `11111111-1111-1111-1111-111111111111`; dev login uses JWT signed with `appsettings.json:Jwt:Key`.

## Securities And Market Data Gotchas
- `POST /api/securities/resolve` accepts only `securityId` or `ticker + exchange`; do not create securities from user-supplied `name/currency/sector` fallback data.
- Metadata refresh and price refresh are separate endpoints: `POST /api/securities/refresh-all` (metadata) and `POST /api/securities/refresh-all-prices` (daily prices).
- Provider priority: TWSE/TPEX use FinMind; NASDAQ/NYSE/AMEX/US prefer YahooFinance, then AlphaVantage fallback.
- YahooFinance is a direct Yahoo chart HTTP provider (browser-like User-Agent + retry/backoff); not the Python `yfinance` package.
- AlphaVantage uses `outputsize=compact`; free-tier `Information`/`Note` responses should be treated as provider failure, not empty success.
- Empty price results must not update `Security.PricesSyncedAtUtc`.
- Symbol quirks are handled in code: DB `BRKB` maps to Yahoo `BRK-B` and AlphaVantage `BRK.B`.

## Risk Analysis
- Portfolio risk: `GET /api/portfolios/{portfolioId}/risk?from=...&to=...&horizonDays=30&confidenceLevel=0.95&simulations=10000`
- Security risk: `GET /api/securities/{securityId}/risk`
- Multi-asset Monte Carlo outputs are in portfolio total market value scale (`MonteCarloMeanFinalValue`, etc.), not unit price.
- Only holdings with a latest market price participate in risk calc; unpriced holdings are counted in `HoldingCount` but excluded from `PricedHoldingCount`.

## Infra And Secrets
- `appsettings.json` contains local connection strings and market-data keys; do not add or commit production secrets.
- Access Garage through its S3-compatible API using `ObjectStorage` config, not provider-specific assumptions.
- Redis is used for cache and Redis Streams background jobs; security search cache keys use the `equitylens:cache` prefix.

## Do Not Commit
Keep these out of Git; they are local data artifacts, not source code:
- `annual-report-chunks*.jsonl`
- `exports/` and `src/EquityLens.Api/exports/`
- `0050_prices.csv`, `mops_response_full.html`
- `法說會/`
- `.playwright-mcp/`
- PDF originals unless explicitly intended as fixtures

## Verification
- Backend build: `dotnet build src/EquityLens.Api/EquityLens.Api.csproj`
- Backend test: `dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj` (xUnit, 141 tests, no integration-test dependencies).
- Frontend build: `npm run build` from `src/EquityLens.Web`.
- If API tests fail with missing PostgreSQL columns, apply EF migrations before debugging service logic.
