# EquityLens.Mathematics

Production VT-GARCH-t + joint-vector FHS calculations for EquityLens. The
ASP.NET API owns input preparation and persistence; this service consumes
versioned jobs through Redis Streams and never reads PostgreSQL directly.

## Local tests

```bash
python -m venv .venv
.venv/bin/pip install -e 'src/EquityLens.Mathematics[test]'
PYTHONPATH=src/EquityLens.Mathematics/src \
  .venv/bin/pytest src/EquityLens.Mathematics/tests
```

## Isolated shadow environment

```bash
docker compose -f docker-compose.risk-shadow.yml up -d postgres-shadow redis-shadow mathematics

ConnectionStrings__PostgreSQL='Host=localhost;Port=55432;Database=equitylens;Username=equitylens;Password=equitylens_dev_password' \
Redis__ConnectionString='localhost:56379' \
RiskPython__ShadowEnabled=true \
dotnet run --no-launch-profile --project src/EquityLens.Api/EquityLens.Api.csproj --urls http://0.0.0.0:5035
```

Apply EF migrations to the shadow database before starting the API. The
existing `equitylens:jobs` stream is not used by Python; the worker only joins
the `risk-python-workers` group on `equitylens:risk-python:jobs`.

Enable the production primary after migrations and smoke tests:

```bash
RiskPython__PrimaryEnabled=true
```

Disable the Python primary immediately with:

```bash
RiskPython__PrimaryEnabled=false
```

New calculations then use the explicit C# MVEWMA-FHS fallback. Completed runs
retain their selected model and fallback reason.

## Research-only adjusted-close pipeline

The adjustment pipeline is intentionally separate from the production
`market_price` table. It consumes an exported
`security_id,ticker,price_date,close` CSV and produces reviewed artifacts:

```bash
PYTHONPATH=src/EquityLens.Mathematics/src \
src/EquityLens.Mathematics/research/backfill_adjusted_close.py \
  --raw-csv /path/to/raw_prices.csv \
  --output-dir /path/to/output \
  --write-source-snapshot /path/to/reviewed-source-snapshot.json
```

Apply `research/adjusted_close_schema.sql` only to a research database, review
all `manual_review` reconciliation rows, and then explicitly load the emitted
CSVs. Zero/absent prices are labeled gaps; they are never interpolated.
For reproducible validation, rerun with `--source-snapshot` pointing to the
reviewed snapshot instead of querying mutable external APIs.
