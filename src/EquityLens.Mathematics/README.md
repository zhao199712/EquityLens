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

## Conditional volatility decomposition

Current-risk and Monte Carlo results include `conditionalRisk` with method
version `vt-garch-residual-correlation-v1`. The existing GARCH fit is reused:
`D = diag(sqrt(next_variances) / 100)`, `R` is the sample correlation of its
standardized residual vectors, and `Sigma = D R D`. Correlation is fixed over
the fitted window; this is not a DCC fit or the covariance of the FHS simulated
simple returns. No extra random sampling, shrinkage, or refitting is used.

`dailyVolatility = sqrt(w' Sigma w)` describes next-trading-day weighted log
returns. `annualizedVolatility` and contribution values multiply this by
`sqrt(252)`; this is a display scale, not a one-year forecast. Both holding and
industry components sum to that annualized value; shares sum to one when risk
is nonzero. Negative components are retained. Zero portfolio volatility yields
null components, shares, and marginal derivatives (not fabricated zeros).

A marginal derivative is per unit weight. For a +1 percentage-point position
increase funded from cash, multiply by 0.01; to display that volatility change
in percentage points, multiply again by 100. Industry marginals add a total
one percentage point in the industry's existing proportions. A zero-weight
industry has no such direction and returns null. Incremental volatility is
the current value minus the value after moving the holding/industry to cash,
without reweighting other assets; it remains defined at zero portfolio risk.

The optional input `assets` array contains `securityId`, `ticker`, `exchange`,
`securityName`, and `industry` in the exact order of `assetReturns` and
`weights`. Metadata is snapshotted by the API when enqueueing, included in the
existing payload hash, and echoed into the persisted result. Missing metadata
is accepted for older jobs, with empty named holdings/industries. Supplied
metadata must match dimensions and have unique security IDs.

Roll out the compatible worker first, then the API and web client. No database
migration or historical-result rewrite is required. Create a new risk run to
verify the primary conditional volatility and both contribution tables; old
Python results explicitly request recalculation and retain historical context.
C# fallback results remain labelled as C# and keep their own model semantics.

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
