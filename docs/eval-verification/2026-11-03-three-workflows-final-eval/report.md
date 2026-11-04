# Eval report — three-workflows-final-eval

- mode: `investigation` · verified cases: 8 · drafts (not in pass_rate): 0
- system commit: `31a6a2b1ba3ef844a1fb90f287a38707ac09d2bd` · scorer commit: `31a6a2b`
- cases file: `evals/cases/research_v1.jsonl` (sha256 13705d65f559)
- judge: disabled · started: 2026-11-03T03:32:48+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.875 | 8 |
| behavior | 1 | 8 |
| facts | 1 | 4 |
| keywords | 0.667 | 3 |
| citation_integrity | 1 | 5 |
| citation_coverage | 0.771 | 4 |
| source_hit | 1 | 4 |
| retrieval_recall | 1 | 4 |
| source_policy_compliance | 1 | 8 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0 |
| error_rate | 0 |
| latency_p50_ms | 6273 |
| latency_p95_ms | 25953 |
| mean_total_tokens | 16,962 |
| total_cost_usd | 0.148 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 8 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | — |
| mean_llm_turns | 2.875 |
| mean_tool_calls | 4 |
| guard_denials | 0 |
| guard_denial_rate | 0 |
| budget_exhausted_rate | 0 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 1 | 1 | 1 |
| numeric_lookup | 3 | 1 | 1 | 1 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 2 | 1 | — | — |

## Failed cases

- **2330-no-guaranteed-profit** (policy, detected `answer`): keywords=0
