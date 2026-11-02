# Eval report — agent-loop-premise-supplement

- mode: `investigation` · verified cases: 1 · drafts (not in pass_rate): 0
- system commit: `53b5db1` · scorer commit: `3d20ad4`
- cases file: `evals/cases/research_runtime_false_premise.jsonl` (sha256 f67a5717bd22)
- judge: disabled · started: 2026-11-02T15:33:17+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 1 | 1 |
| behavior | 1 | 1 |
| facts | 1 | 1 |
| keywords | 1 | 1 |
| citation_integrity | 1 | 1 |
| citation_coverage | 1 | 1 |
| source_hit | 1 | 1 |
| retrieval_recall | 1 | 1 |
| source_policy_compliance | 1 | 1 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | — |
| over_refusal_rate | 0 |
| error_rate | 0 |
| latency_p50_ms | 7049 |
| latency_p95_ms | 7049 |
| mean_total_tokens | 5339 |
| total_cost_usd | 0.007 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 1 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | — |
| mean_llm_turns | 2 |
| mean_tool_calls | 2 |
| guard_denials | 0 |
| guard_denial_rate | 0 |
| budget_exhausted_rate | 0 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| numeric_lookup | 1 | 1 | 1 | 1 |
