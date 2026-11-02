# Eval report — agent-loop-final

- mode: `investigation` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `691a4f4` · scorer commit: `691a4f4`
- cases file: `evals/cases/research_v1.jsonl` (sha256 b2abf837cadf)
- judge: disabled · started: 2026-11-02T14:31:30+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.455 | 11 |
| behavior | 0.455 | 11 |
| facts | 0.167 | 6 |
| keywords | 0.25 | 4 |
| citation_integrity | 1 | 1 |
| citation_coverage | 1 | 1 |
| source_hit | 0.167 | 6 |
| retrieval_recall | 0.833 | 6 |
| source_policy_compliance | 1 | 11 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.857 |
| error_rate | 0 |
| latency_p50_ms | 13049 |
| latency_p95_ms | 26524 |
| mean_total_tokens | 17177 |
| total_cost_usd | 0.287 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 11 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | — |
| mean_llm_turns | 4.364 |
| mean_tool_calls | 6.364 |
| guard_denials | 4 |
| guard_denial_rate | 0.057 |
| budget_exhausted_rate | 0 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 5 | 0.2 | 0.2 | 0.2 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 3 | 1 | — | — |

## Failed cases

- **2330-gross-margin-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2330-eps-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2454-revenue-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2412-cash-dividend-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0, source_hit=0, retrieval_recall=0
- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0, source_hit=0
- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
