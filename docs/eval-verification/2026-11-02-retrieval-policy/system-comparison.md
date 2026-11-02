# Compare: yearfilter-policy-rescored-final → isolated-retrieval-policy-final-v5

| metric | baseline | candidate | Δ |
|---|---|---|---|
| pass_rate | 0.909 | 0.909 | +0.000 |
| behavior | 0.909 | 1 | +0.091 ✅ |
| policy_safety | 1 | 0 | -1.000 ⚠️ |
| facts | 0.833 | 1 | +0.167 ✅ |
| keywords | 1 | 1 | +0.000 |
| citation_integrity | 1 | 1 | +0.000 |
| citation_coverage | 0.853 | 0.697 | -0.156 ⚠️ |
| source_hit | 0.667 | 0.833 | +0.167 ✅ |
| retrieval_recall | 0.833 | 1 | +0.167 ✅ |
| judge_error_count | 0 | 0 | +0.000 |
| over_refusal_case_count | 6 | 6 | +0.000 |
| hallucination_rate | 0 | 0 | +0.000 |
| over_refusal_rate | 0.167 | 0 | -0.167 ✅ |
| error_rate | 0 | 0 | +0.000 |
| latency_p50_ms | 4686 | 5233 | +547.000 ⚠️ |
| latency_p95_ms | 16346 | 13344 | -3,002 ✅ |
| mean_total_tokens | 4,614 | 5,008 | +394.500 ⚠️ |

**Regressions (1)**: 2330-no-guaranteed-profit

**Fixed (1)**: 2317-segment-mix
