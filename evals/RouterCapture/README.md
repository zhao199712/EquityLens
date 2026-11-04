# Live Router capture

This opt-in console harness calls the application's `ConversationService`,
`InvestmentResearchRouter` (including JSON repair), and `AgentWorkflowQueryService`.
It uses per-case EF InMemory fixtures, actual securities/ownership/scenario validation,
and an `IAgentRunService` capture sink. No workers or downstream research/portfolio
LLMs are started. It does **not** test HTTP middleware, PostgreSQL, Redis, persisted
run creation, authentication, report quality, or the production deployment.

The follow-up fixture seeds an owned completed research run and uses the real
conversation entry path. The explicit research fixture calls
`ResearchController.CreateInvestigation` directly. Both must make zero classifier
calls. The explicit controller's unrelated dependencies are absent; middleware
and run-service checks are outside this test's boundary.

Only message/context fields are consumed from the case file. Expected outcomes
are read separately by the existing Python scorer. Raw model requests/responses,
usage, final service outcomes, and predictions are saved for every case. Never
feed real users' private conversations into published eval artifacts.

Build and offline smoke (three fixtures, no paid API):

```bash
dotnet build evals/RouterCapture/RouterCapture.csproj
dotnet evals/RouterCapture/bin/Debug/net10.0/RouterCapture.dll \
  evals/cases/router_v1.jsonl /tmp/router-smoke-new --smoke
```

After obtaining explicit paid API budget authorization, put `DEEPSEEK_API_KEY` in
the process environment (never in a command or repository file), install the eval
Python dependencies, and use a new output directory:

```bash
python evals/RouterCapture/run_budgeted.py \
  --cases evals/cases/router_v1.jsonl --out /tmp/router-live-new --budget-usd 0.50
PYTHONPATH=evals python -m equitylens_eval.router_eval \
  --cases evals/cases/router_v1.jsonl \
  --predictions /tmp/router-live-new/capture/predictions.jsonl \
  --out /tmp/router-live-new
```

The helper always targets the local budget proxy on port 5098; the wrapper forwards
to DeepSeek using the existing conservative peak-rate ledger. Model is
`deepseek-flash`, thinking disabled, temperature/output bound supplied by the
production router. A provider failure is retained, not silently rerun. The
router's own one-repair attempt remains enabled. Existing predictions/ledgers
are never overwritten.
