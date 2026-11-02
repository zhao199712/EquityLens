# Compare: prod-2026-11-02-yearfilter → yearfilter-policy-rescored-final

> ⚠️ The two runs used different case files; metric deltas are not directly comparable.


| metric | baseline | candidate | Δ |
|---|---|---|---|
| pass_rate | 0.818 | 0.909 | +0.091 ✅ |
| behavior | 0.818 | 0.909 | +0.091 ✅ |
| policy_safety | — | 1 | — |
| facts | 0.833 | 0.833 | +0.000 |
| keywords | 0.75 | 1 | +0.250 ✅ |
| citation_integrity | 1 | 1 | +0.000 |
| citation_coverage | 0.853 | 0.853 | +0.000 |
| source_hit | 0.667 | 0.667 | +0.000 |
| retrieval_recall | 0.833 | 0.833 | +0.000 |
| judge_error_count | 0 | 0 | +0.000 |
| hallucination_rate | 0 | 0 | +0.000 |
| over_refusal_rate | 0.286 | 0.167 | -0.119 ✅ |
| error_rate | 0 | 0 | +0.000 |
| latency_p50_ms | 4686 | 4686 | +0.000 |
| latency_p95_ms | 16346 | 16346 | +0.000 |
| mean_total_tokens | 4,614 | 4,614 | +0.000 |
| over_refusal_case_count | — | 6 | — |

**Regressions (0)**: none

**Fixed (1)**: 2330-no-guaranteed-profit
