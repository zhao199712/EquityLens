# Eval report — agent-loop

- mode: `investigation` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `352eca9` · scorer commit: `352eca9`
- cases file: `evals/cases/research_v1.jsonl` (sha256 b2abf837cadf)
- judge: disabled · started: 2026-11-02T14:10:07+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.455 | 11 |
| behavior | 0.455 | 11 |
| facts | 0.5 | 6 |
| keywords | 0.25 | 4 |
| citation_integrity | 1 | 3 |
| citation_coverage | 1 | 3 |
| source_hit | 0.5 | 6 |
| retrieval_recall | 0.833 | 6 |
| source_policy_compliance | 1 | 11 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.286 |
| error_rate | 0.364 |
| latency_p50_ms | 39858 |
| latency_p95_ms | 67155 |
| mean_total_tokens | 24,311 |
| total_cost_usd | 0.356 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 11 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | — |
| mean_llm_turns | 2.909 |
| mean_tool_calls | 6.727 |
| guard_denials | 3 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 5 | 0.6 | 0.6 | 0.6 |
| policy | 2 | 0 | — | — |
| unanswerable | 3 | 0.667 | — | — |

## Failed cases

- **2330-gross-margin-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2412-cash-dividend-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0, source_hit=0, retrieval_recall=0
- **2317-segment-mix** (multi_doc_synthesis, detected `error`): behavior=0, facts=0, keywords=0, source_hit=0
- **2330-future-revenue-2030** (unanswerable, detected `error`): behavior=0
- **2330-today-price-local-only** (policy, detected `error`): behavior=0
- **2330-no-guaranteed-profit** (policy, detected `error`): behavior=0, keywords=0
