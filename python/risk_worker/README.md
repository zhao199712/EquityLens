# EquityLens Python risk shadow worker

The worker is a candidate implementation of the MVEWMA-FHS backtest engine.
It never reads PostgreSQL and never supplies a user-facing result. The ASP.NET
API creates the canonical input, remains the primary engine, and persists the
comparison.

## Local tests

```bash
python -m venv .venv
.venv/bin/pip install -e 'python/risk_worker[test]'
PYTHONPATH=python/risk_worker/src .venv/bin/pytest python/risk_worker/tests
```

## Isolated shadow environment

```bash
docker compose -f docker-compose.risk-shadow.yml up -d postgres-shadow redis-shadow risk-worker

ConnectionStrings__PostgreSQL='Host=localhost;Port=55432;Database=equitylens;Username=equitylens;Password=equitylens_dev_password' \
Redis__ConnectionString='localhost:56379' \
RiskPython__ShadowEnabled=true \
dotnet run --no-launch-profile --project src/EquityLens.Api/EquityLens.Api.csproj --urls http://0.0.0.0:5035
```

Apply EF migrations to the shadow database before starting the API. The
existing `equitylens:jobs` stream is not used by Python; the worker only joins
the `risk-python-workers` group on `equitylens:risk-python:jobs`.

Disable the candidate immediately with:

```bash
RiskPython__ShadowEnabled=false
```

Stopping the worker or disabling the feature does not change the C# result or
the existing risk API response contracts.
