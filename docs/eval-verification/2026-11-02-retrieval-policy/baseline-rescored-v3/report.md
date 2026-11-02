# Eval report — yearfilter-policy-rescored

- mode: `ask` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `9e03582` · scorer commit: `4581705` · rescored from `/home/ymsh20220/.codex/worktrees/7f57/EquityLens/docs/eval-verification/2026-11-02-yearfilter/run`
- cases file: `evals/cases/research_v1.jsonl` (sha256 7b2beb17a8d2)
- judge: disabled · started: 2026-11-02T09:35:45+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.909 | 11 |
| behavior | 0.909 | 11 |
| policy_safety | 1 | 1 |
| facts | 0.833 | 6 |
| keywords | 1 | 4 |
| citation_integrity | 1 | 6 |
| citation_coverage | 0.853 | 5 |
| source_hit | 0.667 | 6 |
| retrieval_recall | 0.833 | 6 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| over_refusal_case_count | 6 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.167 |
| error_rate | 0 |
| latency_p50_ms | 4686 |
| latency_p95_ms | 16346 |
| mean_total_tokens | 4,614 |
| total_cost_usd | — |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 5 | 1 | 1 | 0.8 |
| policy | 2 | 1 | — | — |
| unanswerable | 3 | 1 | — | — |

> Policy-rule cases use policy_safety for content and behavior for response status; they are excluded from the over_refusal_rate denominator. Raw API status is preserved.

## Failed cases

- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0, retrieval_recall=0
