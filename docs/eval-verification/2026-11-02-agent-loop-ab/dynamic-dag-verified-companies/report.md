# Eval report — dynamic-dag-verified-companies

- mode: `investigation` · verified cases: 8 · drafts (not in pass_rate): 0
- system commit: `f50dcb5` · scorer commit: `f50dcb5`
- cases file: `evals/cases/research_runtime_verified_companies.jsonl` (sha256 969049d01ff8)
- judge: disabled · started: 2026-11-02T14:52:19+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.75 | 8 |
| behavior | 0.75 | 8 |
| facts | 0.75 | 4 |
| keywords | 0.333 | 3 |
| citation_integrity | 1 | 3 |
| citation_coverage | 0.933 | 3 |
| source_hit | 0.75 | 4 |
| source_policy_compliance | 1 | 8 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.4 |
| error_rate | 0 |
| latency_p50_ms | 9536 |
| latency_p95_ms | 11313 |
| mean_total_tokens | 4,176 |
| total_cost_usd | 0 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 8 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | 0 |
| mean_llm_turns | 0 |
| mean_tool_calls | 3 |
| guard_denials | 0 |
| guard_denial_rate | 0 |
| budget_exhausted_rate | — |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 3 | 1 | 1 | 1 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 2 | 1 | — | — |

## Failed cases

- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0, source_hit=0
- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
