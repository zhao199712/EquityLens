# Eval report — prod-2026-11-02

- mode: `ask` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `362179d` · scorer commit: `362179d`
- cases file: `cases/research_v1.jsonl` (sha256 b2abf837cadf)
- judge: disabled · started: 2026-11-02T08:26:25+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.545 | 11 |
| behavior | 0.545 | 11 |
| facts | 0.333 | 6 |
| keywords | 0.75 | 4 |
| citation_integrity | 1 | 2 |
| citation_coverage | 1 | 2 |
| source_hit | 0.333 | 6 |
| retrieval_recall | 0.5 | 6 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.714 |
| error_rate | 0 |
| latency_p50_ms | 5190 |
| latency_p95_ms | 16815 |
| mean_total_tokens | 4,926 |
| total_cost_usd | — |

## By category

| category | cases | pass_rate | facts | source_hit |
|---|---|---|---|---|
| multi_doc_synthesis | 1 | 0 | 0 | 0 |
| numeric_lookup | 5 | 0.4 | 0.4 | 0.4 |
| policy | 2 | 0.5 | — | — |
| unanswerable | 3 | 1 | — | — |

## Failed cases

- **2330-gross-margin-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0, retrieval_recall=0
- **2330-eps-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0, retrieval_recall=0
- **2454-revenue-2025** (numeric_lookup, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0
- **2317-segment-mix** (multi_doc_synthesis, detected `insufficient_evidence`): behavior=0, facts=0, source_hit=0, retrieval_recall=0
- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
