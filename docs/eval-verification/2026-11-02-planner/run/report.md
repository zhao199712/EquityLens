# Eval report — prod-2026-11-02-planner

- mode: `ask` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `f1c7a25` · scorer commit: `f1c7a25`
- cases file: `cases/research_v1.jsonl` (sha256 b2abf837cadf)
- judge: disabled · started: 2026-11-02T10:45:08+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.909 | 11 |
| behavior | 0.909 | 11 |
| facts | 1 | 6 |
| keywords | 0.75 | 4 |
| citation_integrity | 1 | 6 |
| citation_coverage | 0.657 | 6 |
| source_hit | 0.833 | 6 |
| retrieval_recall | 1 | 6 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.143 |
| error_rate | 0 |
| latency_p50_ms | 4910 |
| latency_p95_ms | 14557 |
| mean_total_tokens | 4,879 |
| total_cost_usd | — |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 1 | 1 | 1 |
| numeric_lookup | 5 | 1 | 1 | 0.8 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 3 | 1 | — | — |

## Failed cases

- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
