# EquityLens Agent Guide

## Project Summary

EquityLens is an AI-agent investment analysis platform for portfolio tracking, stock analysis, and financial statement analysis.

The project is split into a backend API and a frontend web app:

- Backend: .NET API under `src/EquityLens.Api`
- Frontend: Vue/Vite app under `src/EquityLens.Web`
- Database: PostgreSQL/TimescaleDB with EF Core and pgvector
- Cache/queue infrastructure: Redis
- Object storage: Garage using the S3-compatible API
- Local infrastructure: Docker Compose at `docker-compose.yml`

## Development Commands

From the repository root:

```bash
docker compose up -d
```

Backend:

```bash
dotnet restore src/EquityLens.Api/EquityLens.Api.csproj
dotnet build src/EquityLens.Api/EquityLens.Api.csproj
dotnet run --project src/EquityLens.Api/EquityLens.Api.csproj
```

Frontend:

```bash
cd src/EquityLens.Web
npm install
npm run dev
npm run build
```

## Environment

Use `.env.example` as the reference for local environment values.

Important services:

- PostgreSQL: `${POSTGRES_PORT:-5432}`
- Redis: `${REDIS_PORT:-6379}`
- Garage S3 API: `${S3_API_PORT:-9000}`
- Garage web/admin ports: `${GARAGE_WEB_PORT:-3902}`, `${GARAGE_ADMIN_PORT:-3903}`

Do not commit real credentials, production connection strings, API keys, access keys, or secrets.

## Backend Guidelines

- Keep backend code inside `src/EquityLens.Api`.
- Use nullable reference types consistently; the project has `<Nullable>enable</Nullable>`.
- Use EF Core entities/configurations under `Data/Entities` and `Data/Configurations`.
- Prefer explicit entity configuration over scattered model setup.
- PostgreSQL is the source of truth for investment, portfolio, financial report, and AI-analysis data.
- Use pgvector-related storage only where semantic search, embeddings, or AI document retrieval require it.
- Keep API behavior clear and stable; avoid large refactors unless they directly support the requested change.

## Frontend Guidelines

- Keep frontend code inside `src/EquityLens.Web`.
- Use Vue 3, Vite, TypeScript, Pinia, Vue Router, Naive UI, Axios, and ECharts according to existing patterns.
- Use `src/services/http.ts` for HTTP client behavior where appropriate.
- Keep dashboard, portfolio, stock, and financial-analysis UI flows consistent with existing views.
- Run `npm run build` after meaningful frontend changes when feasible.

## AI-Agent Product Rules

- Treat generated investment output as analysis support, not guaranteed financial advice.
- Preserve traceability for financial statement analysis, document citations, embeddings, and AI memos.
- Prefer designs that make assumptions, data source, timestamp, and confidence visible.
- Avoid silently fabricating market data, financial line items, or portfolio values.
- When adding AI features, keep source documents, citations, and model outputs separable.

## Infrastructure Guidelines

- Use Docker Compose for local PostgreSQL, Redis, and Garage.
- Garage should be accessed through its S3-compatible API rather than provider-specific assumptions.
- Do not change service ports or default credentials unless the task explicitly requires it.
- Keep local-only configuration out of committed source files.

## Verification

For backend changes, prefer:

```bash
dotnet build src/EquityLens.Api/EquityLens.Api.csproj
```

For frontend changes, prefer:

```bash
cd src/EquityLens.Web
npm run build
```

If verification cannot be run, state why and describe the residual risk.

## Agent Behavior

- Inspect existing code before changing structure or naming.
- Make the smallest correct change.
- Do not rewrite unrelated files.
- Do not modify generated folders such as `bin`, `obj`, or frontend build output.
- Ask before introducing new major dependencies, external services, or cross-cutting architecture changes.
