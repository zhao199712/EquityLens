# Eval report — prod-2026-11-02-reembed

- mode: `ask` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `0b97463` · scorer commit: `0b97463`
- cases file: `cases/research_v1.jsonl` (sha256 b2abf837cadf)
- judge: disabled · started: 2026-11-02T09:19:23+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.636 | 11 |
| behavior | 0.636 | 11 |
| facts | 0.5 | 6 |
| keywords | 0.5 | 4 |
| citation_integrity | 1 | 3 |
| citation_coverage | 0.6 | 3 |
| source_hit | 0.5 | 6 |
| retrieval_recall | 0.833 | 6 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.571 |
| error_rate | 0 |
| latency_p50_ms | 4690 |
| latency_p95_ms | 18035 |
| mean_total_tokens | 5,201 |
| total_cost_usd | — |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 5 | 0.6 | 0.6 | 0.6 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 3 | 1 | — | — |

## Failed cases

- **2330-eps-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2454-revenue-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, keywords=0, source_hit=0, retrieval_recall=0
- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
