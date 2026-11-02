# Eval report — agent-loop-verified-companies

- mode: `investigation` · verified cases: 8 · drafts (not in pass_rate): 0
- system commit: `f50dcb5` · scorer commit: `f50dcb5`
- cases file: `evals/cases/research_runtime_verified_companies.jsonl` (sha256 969049d01ff8)
- judge: disabled · started: 2026-11-02T14:53:51+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.625 | 8 |
| behavior | 0.625 | 8 |
| facts | 0.5 | 4 |
| keywords | 0.667 | 3 |
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
| latency_p50_ms | 11983 |
| latency_p95_ms | 20404 |
| mean_total_tokens | 10,186 |
| total_cost_usd | 0.123 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 8 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | — |
| mean_llm_turns | 3.625 |
| mean_tool_calls | 5.5 |
| guard_denials | 10 |
| guard_denial_rate | 0.227 |
| budget_exhausted_rate | 0 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 1 | 1 | 1 |
| numeric_lookup | 3 | 0.333 | 0.333 | 0.333 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 2 | 1 | — | — |

## Failed cases

- **2330-gross-margin-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2330-eps-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
