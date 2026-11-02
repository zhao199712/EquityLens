# Eval report — dynamic-dag

- mode: `investigation` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `352eca9` · scorer commit: `352eca9`
- cases file: `evals/cases/research_v1.jsonl` (sha256 b2abf837cadf)
- judge: disabled · started: 2026-11-02T14:05:14+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.727 | 11 |
| behavior | 0.727 | 11 |
| facts | 0.667 | 6 |
| keywords | 0.625 | 4 |
| citation_integrity | 1 | 4 |
| citation_coverage | 0.8 | 4 |
| source_hit | 0.667 | 6 |
| source_policy_compliance | 1 | 11 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.429 |
| error_rate | 0 |
| latency_p50_ms | 22783 |
| latency_p95_ms | 49650 |
| mean_total_tokens | 5,084 |
| total_cost_usd | 0 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 11 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | 0 |
| mean_llm_turns | 0 |
| mean_tool_calls | 3 |
| guard_denials | 0 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 5 | 0.8 | 0.8 | 0.8 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 3 | 1 | — | — |

## Failed cases

- **2412-cash-dividend-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0.5, source_hit=0
- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
