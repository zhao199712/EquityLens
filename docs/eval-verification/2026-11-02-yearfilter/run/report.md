# Eval report — prod-2026-11-02-yearfilter

- mode: `ask` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `9e03582` · scorer commit: `9e03582`
- cases file: `cases/research_v1.jsonl` (sha256 b2abf837cadf)
- judge: disabled · started: 2026-11-02T09:35:45+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.818 | 11 |
| behavior | 0.818 | 11 |
| facts | 0.833 | 6 |
| keywords | 0.75 | 4 |
| citation_integrity | 1 | 5 |
| citation_coverage | 0.853 | 5 |
| source_hit | 0.667 | 6 |
| retrieval_recall | 0.833 | 6 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.286 |
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
| policy | 2 | 0.5 | — | — |
| unanswerable | 3 | 1 | — | — |

## Failed cases

- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0, retrieval_recall=0
- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
