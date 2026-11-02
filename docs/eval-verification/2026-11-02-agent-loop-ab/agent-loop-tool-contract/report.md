# Eval report — agent-loop-tool-contract

- mode: `investigation` · verified cases: 8 · drafts (not in pass_rate): 0
- system commit: `1910da2` · scorer commit: `1910da2`
- cases file: `evals/cases/research_runtime_verified_companies.jsonl` (sha256 969049d01ff8)
- judge: disabled · started: 2026-11-02T15:02:35+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.625 | 8 |
| behavior | 0.625 | 8 |
| facts | 0.5 | 4 |
| keywords | 0.333 | 3 |
| citation_integrity | 1 | 2 |
| citation_coverage | 1 | 2 |
| source_hit | 0.5 | 4 |
| retrieval_recall | 1 | 4 |
| source_policy_compliance | 1 | 8 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.6 |
| error_rate | 0 |
| latency_p50_ms | 11535 |
| latency_p95_ms | 14913 |
| mean_total_tokens | 10,623 |
| total_cost_usd | 0.078 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 8 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | — |
| mean_llm_turns | 2.875 |
| mean_tool_calls | 3.375 |
| guard_denials | 2 |
| guard_denial_rate | 0.074 |
| budget_exhausted_rate | 0 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 3 | 0.667 | 0.667 | 0.667 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 2 | 1 | — | — |

## Failed cases

- **2330-gross-margin-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0, source_hit=0
- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
