# Eval report — agent-loop-bounded

- mode: `investigation` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `11ce402` · scorer commit: `11ce402`
- cases file: `evals/cases/research_v1.jsonl` (sha256 b2abf837cadf)
- judge: disabled · started: 2026-11-02T14:21:31+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.818 | 11 |
| behavior | 0.818 | 11 |
| facts | 0.667 | 6 |
| keywords | 0.5 | 4 |
| citation_integrity | 1 | 5 |
| citation_coverage | 0.933 | 5 |
| source_hit | 0.5 | 6 |
| retrieval_recall | 0.667 | 6 |
| source_policy_compliance | 1 | 11 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.286 |
| error_rate | 0 |
| latency_p50_ms | 13629 |
| latency_p95_ms | 30162 |
| mean_total_tokens | 16,090 |
| total_cost_usd | 0.31 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 11 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | — |
| mean_llm_turns | 4.182 |
| mean_tool_calls | 6.545 |
| guard_denials | 5 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 5 | 0.8 | 0.8 | 0.6 |
| policy | 2 | 1 | — | — |
| unanswerable | 3 | 1 | — | — |

## Failed cases

- **2412-cash-dividend-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0, source_hit=0, retrieval_recall=0
- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0, source_hit=0
