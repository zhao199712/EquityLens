# Eval report — three-workflows-live

- mode: `investigation` · verified cases: 8 · drafts (not in pass_rate): 0
- system commit: `404df167e5a79e752bc148f496a3d514f0b0b486` · scorer commit: `404df16`
- cases file: `evals/cases/research_v1.jsonl` (sha256 13705d65f559)
- judge: disabled · started: 2026-11-03T03:01:42+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.75 | 8 |
| behavior | 0.875 | 8 |
| facts | 0.75 | 4 |
| keywords | 0.667 | 3 |
| citation_integrity | 0.75 | 4 |
| citation_coverage | 0.722 | 3 |
| source_hit | 0.75 | 4 |
| retrieval_recall | 1 | 4 |
| source_policy_compliance | 1 | 8 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.2 |
| error_rate | 0 |
| latency_p50_ms | 8841 |
| latency_p95_ms | 27595 |
| mean_total_tokens | 16,723 |
| total_cost_usd | 0.11 |

## Trajectory

| metric | value |
|---|---|
| recorded_runs | 8 |
| web_policy_violation_count | 0 |
| planner_fallback_rate | — |
| mean_llm_turns | 3 |
| mean_tool_calls | 3.25 |
| guard_denials | 0 |
| guard_denial_rate | 0 |
| budget_exhausted_rate | 0 |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 3 | 1 | 1 | 1 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 2 | 1 | — | — |

## Failed cases

- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0, source_hit=0
- **2330-no-guaranteed-profit** (policy, detected `answer`): citation_integrity=0
