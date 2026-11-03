# Eval report — agent-loop-unit-verification

- mode: `investigation` · verified cases: 8 · drafts (not in pass_rate): 0
- system commit: `53b5db1` · scorer commit: `53b5db1`
- cases file: `evals/cases/research_runtime_verified_companies.jsonl` (sha256 969049d01ff8)
- judge: disabled · started: 2026-11-02T15:15:26+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.875 | 8 |
| behavior | 1 | 8 |
| facts | 1 | 4 |
| keywords | 1 | 3 |
| citation_integrity | 0.8 | 5 |
| citation_coverage | 0.875 | 4 |
| source_hit | 0.75 | 4 |
| retrieval_recall | 0.75 | 4 |
| source_policy_compliance | 1 | 8 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0 |
| error_rate | 0 |
| latency_p50_ms | 9324 |
| latency_p95_ms | 11486 |
| mean_total_tokens | 7,615 |
| total_cost_usd | 0.055 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 8 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | — |
| mean_llm_turns | 2.5 |
| mean_tool_calls | 3 |
| guard_denials | 5 |
| guard_denial_rate | 0.208 |
| budget_exhausted_rate | 0 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 1 | 1 | 1 |
| numeric_lookup | 3 | 1 | 1 | 0.667 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 2 | 1 | — | — |

## Failed cases

- **2330-no-guaranteed-profit** (policy, detected `answer`): citation_integrity=0
