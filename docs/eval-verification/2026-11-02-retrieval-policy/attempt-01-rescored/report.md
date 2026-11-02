# Eval report — attempt-01-rescored

- mode: `ask` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `dda2e55f21212ea7f3cbab2548c8237457989a53` · scorer commit: `4581705` · rescored from `/home/ymsh20220/.codex/worktrees/7f57/EquityLens/docs/eval-verification/2026-11-02-retrieval-policy/attempt-01`
- cases file: `evals/cases/research_v1.jsonl` (sha256 7b2beb17a8d2)
- judge: disabled · started: 2026-11-02T10:07:34+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 1 | 11 |
| behavior | 1 | 11 |
| policy_safety | 1 | 1 |
| facts | 1 | 6 |
| keywords | 1 | 4 |
| citation_integrity | 1 | 7 |
| citation_coverage | 0.619 | 6 |
| source_hit | 0.833 | 6 |
| retrieval_recall | 1 | 6 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| over_refusal_case_count | 6 |
| hallucination_rate | 0 |
| over_refusal_rate | 0 |
| error_rate | 0 |
| latency_p50_ms | 4284 |
| latency_p95_ms | 16453 |
| mean_total_tokens | 4,967 |
| total_cost_usd | — |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 1 | 1 | 1 |
| numeric_lookup | 5 | 1 | 1 | 0.8 |
| policy | 2 | 1 | — | — |
| unanswerable | 3 | 1 | — | — |

> Policy-rule cases use policy_safety for content and behavior for response status; they are excluded from the over_refusal_rate denominator. Raw API status is preserved.
