# Eval report — restored-scorer

- mode: `ask` · verified cases: 11 · drafts (not in pass_rate): 0
- system commit: `e741c24086ec76849b0d3e0aa8a20b920fce0a22` · scorer commit: `1a720a5` · rescored from `/home/ymsh20220/.codex/worktrees/7f57/EquityLens/docs/eval-verification/2026-11-02-retrieval-policy/run`
- cases file: `evals/cases/research_v1.jsonl` (sha256 b2abf837cadf)
- judge: disabled · started: 2026-11-02T10:22:00+00:00

## Quality

| metric | mean | n |
|---|---|---|
| pass_rate | 0.909 | 11 |
| behavior | 0.909 | 11 |
| facts | 1 | 6 |
| keywords | 0.75 | 4 |
| citation_integrity | 1 | 6 |
| citation_coverage | 0.697 | 6 |
| source_hit | 0.833 | 6 |
| retrieval_recall | 1 | 6 |

## Behaviour, cost and latency

| metric | value |
|---|---|
| judge_error_count | 0 |
| hallucination_rate | 0 |
| over_refusal_rate | 0.143 |
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

## Failed cases

- **2330-no-guaranteed-profit** (policy, detected `insufficient_evidence`): behavior=0, keywords=0
