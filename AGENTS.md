# EquityLens Agent Guide

## Environment Assumptions
- Development and verification are done on Linux. Commands assume a Linux shell (bash) unless stated otherwise.

## Repo Shape
- Backend API lives in `src/EquityLens.Api` (`net10.0`, nullable enabled, implicit usings); entrypoint and DI wiring are in `Program.cs`.
- Frontend app lives in `src/EquityLens.Web` (Vue 3 + Vite + TypeScript); entrypoints are `src/main.ts`, `src/App.vue`, and `src/router/index.ts`.
- Local infra is `docker-compose.yml`: PostgreSQL/TimescaleDB, Redis, Garage S3 API, and Garage Web UI.

## Commands
- Start local infra from repo root: `docker compose up -d`.
- Backend restore/build/run: `dotnet restore src/EquityLens.Api/EquityLens.Api.csproj`, `dotnet build src/EquityLens.Api/EquityLens.Api.csproj`, `dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj`.
- Backend test: `dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj`.
- API dev URL is `http://localhost:5034`; Swagger is `http://localhost:5034/swagger` and only maps in `Development`.
- `dotnet-ef` is not built-in; install with `dotnet tool install --global dotnet-ef`.
- Apply EF migrations from repo root: `dotnet ef database update --project src/EquityLens.Api/EquityLens.Api.csproj --startup-project src/EquityLens.Api/EquityLens.Api.csproj`.
- Design-time DB connection is hardcoded in `Data/DesignTimeDbContextFactory.cs`; if migration fails on connection, check that file.
- Frontend commands from `src/EquityLens.Web`: `npm install`, `npm run dev`, `npm run build`; `npm run build` runs `vue-tsc -b && vite build`.

## Backend Conventions
- Keep EF entities in `Data/Entities` and explicit mappings in `Data/Configurations`; migrations live in `src/EquityLens.Api/Migrations` and must be applied before testing new columns.
- Repository/service/controller patterns are already established; prefer extending existing repositories and services over adding cross-cutting abstractions.
- XML documentation comments in backend controllers/services have been written in Traditional Chinese; keep that style when adding public API comments.
- Do not edit `bin`, `obj`, frontend build output, or generated EF snapshot content except through migrations.

## Frontend Gotchas
- The axios `http` service default `baseURL` is `http://localhost:8080`; backend dev server runs on `http://localhost:5034`. Set `VITE_API_BASE_URL` env var or configure a proxy.
- Backend CORS policy only allows `http://localhost:5173` (Vite dev server).
- Vue router `beforeEach` auth guard is **commented out**; routes do not check authentication.
- Demo user (`11111111-1111-1111-1111-111111111111`) has no login password. For dev test, sign a JWT with the dev key from `appsettings.json:Jwt:Key`, or register a new user via `/api/auth/register`.
- `Kimi_Agent_科技感投資前端/` is a React + shadcn prototype (separate Vite project), not the main Vue frontend.

## Risk Analysis Endpoints
- Portfolio risk: `GET /api/portfolios/{portfolioId}/risk?from=...&to=...&horizonDays=30&confidenceLevel=0.95&simulations=10000`
- Security risk (single-asset MC): `GET /api/securities/{securityId}/risk`
- Multi-asset Monte Carlo final values are in portfolio total market value scale (not unit price), output as `MonteCarloMeanFinalValue`, `MonteCarloWorstCaseFinalValue`, etc.
- Only holdings with a latest market price (`pricedHoldings`) participate in risk calculation; holdings without prices are counted in `HoldingCount` but excluded from `PricedHoldingCount`.

## Securities And Market Data Gotchas
- `POST /api/securities/resolve` accepts only `securityId` or `ticker + exchange`; it must not create securities from user-supplied `name/currency/sector` fallback data.
- Manual `POST /api/securities` creation was removed; securities should come from external providers.
- Metadata refresh and price refresh are intentionally separate: `/api/securities/refresh-all` updates metadata only, while `/api/securities/refresh-all-prices` updates daily prices.
- Price provider priority is market-specific: TWSE/TPEX use FinMind; NASDAQ/NYSE/AMEX/US prefer YahooFinance then AlphaVantage fallback.
- YahooFinance is a direct Yahoo chart HTTP provider, not the Python `yfinance` package; it uses browser-like User-Agent, retry/backoff, and batch refresh delay to reduce 429s.
- AlphaVantage uses `outputsize=compact`; free tier can rate-limit or return `Information/Note`, which should be treated as provider failure, not empty success.
- Empty price results must not update `Security.PricesSyncedAtUtc`; otherwise a security looks synced while `market_price` has no rows.
- Provider symbol quirks are handled in code: DB ticker `BRKB` maps to Yahoo `BRK-B` and AlphaVantage `BRK.B`.

## Infra And Secrets
- `appsettings.json` currently contains local connection strings and market-data keys; do not add or commit real production secrets.
- Garage should be accessed through its S3-compatible API (`ObjectStorage` config), not provider-specific assumptions.
- Redis is used for cache and Redis Streams background jobs; security search cache keys use the `equitylens:cache` prefix.

## Verification
- Backend build: `dotnet build src/EquityLens.Api/EquityLens.Api.csproj`.
- Backend test: `dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj` (xunit, 120+ tests, no integration test dependencies).
- Frontend build: `npm run build` from `src/EquityLens.Web` (runs `vue-tsc -b && vite build`).
- If an API test fails with missing PostgreSQL columns, apply EF migrations before debugging service logic.
