# Eval report — isolated-retrieval-policy-final-v5

- mode: `ask` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `e741c24086ec76849b0d3e0aa8a20b920fce0a22` · scorer commit: `e741c24`
- cases file: `evals/cases/research_v1.jsonl` (sha256 7b2beb17a8d2)
- judge: disabled · started: 2026-11-02T10:22:00+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.909 | 11 |
| behavior | 1 | 11 |
| policy_safety | 0 | 1 |
| facts | 1 | 6 |
| keywords | 1 | 4 |
| citation_integrity | 1 | 7 |
| citation_coverage | 0.697 | 6 |
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
| latency_p50_ms | 5233 |
| latency_p95_ms | 13344 |
| mean_total_tokens | 5,008 |
| total_cost_usd | — |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 1 | 1 | 1 |
| numeric_lookup | 5 | 1 | 1 | 0.8 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 3 | 1 | — | — |

> Policy-rule cases use policy_safety for content and behavior for response status; they are excluded from the over_refusal_rate denominator. Raw API status is preserved.

## Failed cases

- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): policy_safety=0
