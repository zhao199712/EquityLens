# EquityLens.Mathematics relocation

The Python risk project moved from `python/risk_worker` to
`src/EquityLens.Mathematics`. Its import namespace is now
`equitylens_mathematics`.

Historical validation reports and execution logs intentionally retain their
original paths and hashes. Those references identify the source layout at the
time each validation artifact was produced and must not be interpreted as
current run instructions.

Current commands:

```bash
pip install -e 'src/EquityLens.Mathematics[test,research]'
PYTHONPATH=src/EquityLens.Mathematics/src \
  pytest src/EquityLens.Mathematics/tests
docker compose up -d redis mathematics
```
