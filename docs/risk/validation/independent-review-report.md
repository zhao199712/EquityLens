# Independent review: PR #29 risk-model validation (branch `ccr-feb6f7da-7zjwq5`)

Reviewer scope: production risk changes (C# alignment / return basis, Python validator, current-risk fields), the research replay (`es_backtest.py`, `attribution.py`, `fhs_tail.py` header only), and the reports under `docs/risk/validation/`, chiefly `var-attribution-report.md`. The repo was not modified. All scripts and outputs are in
`docs/risk/validation/independent-review/` (`scripts/`, `results/*.json`). The standalone C# harness (`align-cs/`) is not committed; its leading-gap case is now covered by `PortfolioRiskDataPreparerTests.AllPreparationModes_CarryForwardWhenAnAssetLacksAPriceOnTheCommonStartDate`.

## Experiments run

| ID | What | Script | Output |
|---|---|---|---|
| E2 | **Correct DGP passed through the production replay.** 2 assets, GARCH(1,1) marginals, iid joint standardised multivariate-t(6) vectors (corr 0.5). This is exactly the structure VT-GARCH-t plus joint-vector FHS assumes. Realised returns come from the DGP, independent of the forecasts. Forecasts come from `collect_forecasts_multi` (production fit cadence, filtering, 10,000 draws, nearest rank), with parameters estimated. Weights 0.76/0.24 ("conc") and 0.5/0.5. 160 reps × T=1500. | `scripts/e2_garch_dgp.py` | `results/e2.json` |
| E2-long | Same as E2 with T=5,375 (the long9 length), 40 reps | same, `OOS=5375` | `results/e2_long.json` |
| E3 | Known volatility, iid t(5), with the **production rolling 252-day bank** (overlapping across days; today's return enters tomorrow's bank), resampled 10,000×. No estimation. 200 reps × T=1500 | `scripts/e3_rolling_bank.py` | `results/e3.json` |
| E4 | E3 structure at T=5,375 and 1,817 (400 reps each), recording breach counts and the U_ES statistic mean H, compared with the observed values | `scripts/e4_rolling_null.py` | `results/e4.json` |
| G | Returns that the eval export drops, compared with production carry-forward | `scripts/gaps.py` | stdout (quoted below) |
| C# | Standalone harness for `RiskPriceAlignment.Align` edge cases | `align-cs/Program.cs` | stdout (quoted below) |

Summary tables for E2/E2-long/E3 come from `scripts/summarise.py`. The rejection rate is the share of replications with p < .05.

Rejection rates under a correct model (nominal 5%):

| Test | E3 (known σ, rolling bank, T=1500) 95/97.5/99 | E2 (estimated, T=1500, conc) | E2-long (T=5375, conc / equal) |
|---|---|---|---|
| Binomial vs order-stat rate (report's H_A test) | 0 / 0 / 0 | 0 / 0 / 0.6% | 0 / 0 / 0 ; 0 / 2.5% / 0 |
| Kupiec vs nominal | 0 / 0.5% / 1% | 1.3 / 2.5 / 11% | 5 / 17.5 / **42.5%** ; 2.5 / 35 / **50%** |
| Christoffersen | 6.5 / 1.5 / 0.5% | 6.9 / 2.5 / 1.9% | 5 / 7.5 / 0 ; 7.5 / 10 / 0 |
| **U_ES, exchangeable null** | **0 / 0 / 0** | **0.6 / 1.9 / 1.9%** | **2.5 / 0 / 5% ; 0 / 0 / 2.5%** |
| C_ES(1) exchangeable | 4.5 / 3 / 4% | 3.7 / 3.7 / 5% | 7.5 / 12.5 / 7.5 ; 7.5 / 5 / 2.5% |
| C_ES(5) exchangeable | 4.5 / 6 / 4.5% | 5 / 6.2 / 5.6% | 7.5 / 5 / 5 ; 2.5 / 7.5 / 2.5% |
| U_ES, model-own null | 16 / 65.5 / 100% | 47.5 / 87 / 100% | 100 / 100 / 100% |
| C_ES(1)/(5), model-own | 4.5/4, 2.5/5, 6/2% | 5.6/5.6, 3.7/4.4, 8.1/2.5% | 2.5–12.5% |

Pooled breach rate of production nearest-rank VaR under the correct model:

| | 95% | 97.5% | 99% |
|---|---|---|---|
| Report's "exact" rate (n=252) | 5.19% | 2.70% | 1.20% |
| E3 (known σ) | 5.20% (5.12–5.28) | 2.71% (2.66–2.77) | 1.18% (1.14–1.22) |
| E2 conc, T=1500, estimated | **5.30% (5.21–5.39)** | **2.81% (2.74–2.88)** | **1.30% (1.26–1.35)** |
| E2-long conc / equal, T=5375 | 5.35 / 5.33% | 2.85 / 2.87% | 1.28 / 1.27% |

Breach-count dispersion under the correct model is far below binomial. At T=5,375 (E2-long), the 95% count SD is 8.3 against a binomial SD of 16.5, and the 99% SD is 3.8–5.0 against 8.2. E4 (known σ) gives SD/binomial ratios of 0.40–0.50.

---

## Findings

### F1. BLOCKING: the exchangeable U_ES null is badly undersized, so "ES has no reliable under-estimation evidence" is unsupported
- **Claim:** `var-attribution-report.md` §1.2 and §3.2: "with the exchangeable null U_ES rejects in only 1 of 12 … **ES 的頻率加深度沒有可靠的低估證據**" ("no reliable evidence that ES frequency-plus-depth is under-estimated"). Also "U_ES … 誤拒率為 5%–7.5%" ("false-rejection rate 5%–7.5%").
- **Problem:** `du_escanciano_exchangeable` draws each day's rank **independently** across days. Production banks are rolling 252-day windows, and each realised residual enters the next day's bank. Ranks are therefore negatively dependent across days: a run of low returns lowers the following quantiles. The independent-day null then has far too much variance. The author's size check (`test_attribution._calibrated_scenario_days` and the report's "same kind of simulation") uses a **fresh, independent bank every day**, which removes exactly this dependence. The size check is therefore circular: it validates the null under its own independence assumption.
- **Evidence:** the U_ES exchangeable rejection rate is 0% in E3 (200 reps), 0.6–1.9% in E2 (160 reps) and 0–5% in E2-long. In E3 the U_ES p-values have mean 0.49 but minimum 0.07–0.13 over 200 reps, so the test essentially cannot reject.
  - E4 shows the mean-H null SD under a rolling bank is about 0.00052 at T=5,375. Against that null the observed mean H is significant for long9 equal (95% p=.0025, 97.5% p=.0025, 99% p=.045), long9 snapshot 95% (p=.0025), top10 equal (all three levels p ≤ .015) and top10 snapshot 95% (p=.037). Observed values come from `es-backtest-v3/es_backtest_summary.json`.
  - E4 assumes known σ, so these p-values are illustrative rather than final. They do show that the 1/12 rejection count reflects the test's lack of power, not the absence of a signal.
- **Fix:** simulate the null by re-running the actual rolling-bank / refit pipeline on a correctly specified DGP (a parametric bootstrap, as in E2), or use a block-permutation scheme that preserves the bank overlap. Until then, report U_ES as inconclusive rather than as evidence against under-estimation. Add a unit test with an overlapping bank.

### F2. MAJOR: the H_A argument for "VaR under-coverage is mainly finite-sample bias, not model error" is not established
Four separate problems:
1. **The "exact" rate is not exact for production.** The j/(n+1) argument needs the realised standardised residual to be exchangeable with the bank residuals. That holds only when the residuals are the true innovations. In production they come from estimated GARCH parameters: in-sample filtered residuals, the variance path initialised at the target (`garch.py` `path[0] = target`), and out-of-sample σ forecast error. In E2, with a correctly specified DGP and estimated parameters, the breach rate is about 5.30–5.35 / 2.81–2.87 / 1.27–1.30%. Each 95% CI excludes 5.19 / 2.70 / 1.20. The unit test (`test_order_statistic_rate_matches_simulated_production_breach_rate`) uses fresh, known-distribution banks and cannot detect this.
2. **The binomial test against the order-statistic rate has the wrong variance.** Under the production structure, breach counts are strongly under-dispersed: SD is 0.4–0.6× binomial (E2, E2-long, E4). The test rejected 0–2.5% of correct-model replications, and it can never be "significant" for modest excesses. It also has only about 9.5% power to tell 5.19% from 5.00% at T=5,375 (my binomial power calculation). The same test therefore could not distinguish H_A from its own alternative. The "Kupiec on the unbiased VaR passes" check has the same weakness.
3. **Re-tested against the E2-long correct-model null** (mean and SD from 40 reps, normal approximation):
   - Long9 snapshot is fully consistent with a correct model (z = −0.4 to 1.0).
   - **Long9 equal-weight is not:** 95% z=1.5–1.7 (p≈.04–.06), **97.5% z=2.2–2.7 (p≈.003–.014)**, 99% z=1.3–1.7 (p≈.04–.10).
   - So "八組皆無顯著差異" ("no significant difference in any of the eight groups") does not survive a correctly calibrated null for the diversified portfolio.
   - Caveat: this null depends on the DGP parameters I chose (α≈.07–.10, β≈.86–.91, ν=6). It is not distribution-free; that is the point.
4. **"主要由此偏誤造成" ("mainly caused by this bias") is quantitatively wrong even on the report's own numbers.** The bias (0.19–0.20 pp) explains about 38% of the mean excess over nominal across the 12 cells (mean excess 0.52 pp). In the four Kupiec-rejected cells it explains only 23–50% (top10 snapshot 97.5%: 0.20 of 0.86 pp; long9 equal 97.5%: 0.20 of 0.63 pp). Also, 11 of 12 observed rates exceed the order-statistic rate, and 10 of 12 unbiased-VaR rates exceed nominal.
5. **Framing.** Nearest-rank on 252 atoms is a property of the production VaR estimator. The production VaR does under-cover, so calling this "not a model error" (不是模型錯誤) invites "no action". The recommendation in §6.1 to fix it is right; the headline is not.
- **Fix:**
  - Calibrate the breach-count null by parametric bootstrap of the full pipeline (E2-style, ideally at fitted long9 parameters).
  - Report the bias share of the excess rather than a binary "established".
  - Reword §1.1 and §3.1 as "about 0.2 pp of the 0.2–0.9 pp excess is a deterministic estimator bias; the remainder is (for the concentrated portfolio) consistent with estimation noise under a correct model, and (for equal weight) marginally significant."
  - In §2.1, note that j/(n+1) ignores parameter estimation.

### F3. MAJOR (factual): the size-simulation evidence behind the retraction is thin and not committed
- **Problem:** the §3.2 table (U_ES 100%, Z2 88%, Z1 50%, MF 0%, C_ES 0%) comes from "8 組" ("8 groups"), i.e. 8 replications. The Wilson 95% CI for 4/8 is 22–78%. The "5%–7.5%" exchangeable figure gives no replication count. No script for either is committed (`grep exchangeable` finds only the library, the attribution module and the unit test), so the numbers cannot be reproduced.
- **Agreement:** my E2/E3 independently confirm the qualitative conclusion that the model-own U_ES null is grossly oversized (47–100% rejection). That retraction is correct. I did not re-run Z1/Z2.
- **Fix:** commit the simulation script, use ≥200 reps, and use the production rolling-bank structure (F1).

### F4. MAJOR: crash in `RiskPriceAlignment.Align` when an asset has no row on the common start date
- **Location:** `src/EquityLens.Api/Services/RiskAnalysis/RiskPriceAlignment.cs`, the `else` branch `values.Add(previous!.Value)`. The comment says "start 為所有資產首個價格日之最大者，因此此處必有前值" ("start is the latest of the assets' first price dates, so a previous value always exists here").
- **Problem:** `previous` is only filled from dates inside `[start, end]`. If asset A has history before `start` but no row exactly on `start`, the first iteration has `previous == null`. Example: A is suspended on the day B's data begins, or B's first row comes after a vendor gap on a day A also lacks.
- **Evidence:** the harness `align-cs/Program.cs` case `leading-gap` gives A = {01-02, 01-04, 01-05} and B = {01-03, 01-04, 01-05}. It throws `InvalidOperationException: Nullable object must have a value.` This is an unhandled 500 in all three preparer paths. The Python mirror `align_carry_forward` has the same flaw: it assigns `None` into a float array.
- **Fix:** seed `previous` with the last valid price at or before `start` (rows before `start` are already loaded). Alternatively, define `start` as the first date on which every asset has a valid row. Add a test.

### F5. MINOR/MAJOR (production semantics): carry-forward has no limit, is not disclosed, and changes mixed-calendar behaviour
- `CarriedForwardCounts` is computed but used nowhere (`grep` finds no consumer). There is no cap on run length, so a multi-month suspension feeds GARCH a run of exact zeros and then one catch-up jump, with no warning to the user.
- **Mixed calendars:** the app supports TWSE/TPEX together with NYSE/NASDAQ. Union-of-dates turns each market's holidays into zero returns for the other market's assets. The harness case `mixed-calendar` shows two weekend-style days carried forward. The old intersection compressed those days into multi-day returns instead. Neither choice is clearly right, but the change alters variance and correlation estimates for cross-market portfolios and is not discussed.
- A day on which **no** held asset has a price still collapses into a two-session return. G found exactly this case: 2025-07-31 → 2025-08-04 across a missing 2025-08-01 for all 9 assets. The class comment "避免把多個交易日的變動壓成單一日報酬" ("avoids compressing several trading days' moves into a single daily return") overstates the guarantee.
- **Fix:** cap the carry-forward length or fail on it, surface the fill counts in the response or warnings, and document the cross-market policy.

### F6. MINOR: other `Align` edge cases
Results from the C# harness:
- **Null adjusted close on the first in-window row:** the value becomes `0m` (`previous ?? 0m`). The backtest path then rejects the input through `HasUnavailablePrice`, which is fine. With the `AdjustedClose ?? Close` selector this cannot happen. The 0 is a fragile sentinel that relies on the later `<= 0` check.
- **Duplicate dates:** the last row in input order wins (the harness shows A=105 for two 01-02 rows). The repository orders by `PriceTime` and filters `Interval == "1d"`, so this is acceptable, but it is silent.
- **Null adjusted close mid-series with the `?? Close` selector** (legacy risk and Monte Carlo paths): raw close is mixed into an adjusted series (harness `null-mid-fallback`: 100 → 300 → 102). This is pre-existing, but it is now reachable on the union calendar too.
- **Zero or negative prices:** checked after alignment in all three paths. OK.
- **Trailing data:** `end = min(last dates)` truncates every asset at the earliest last date, with no extrapolation. OK, but one stale asset silently shortens the history.

### F7. MINOR: research inputs vs production
- **Missing-price policy (drop vs carry-forward):** G shows this is **immaterial** for long9/top10 in v3. There are no null cells except one all-asset session (2025-08-01). The export drops one return (−1.73% snapshot) that production would keep as a two-session return. There is 1 differing day out of 5,628. The quarantine list is empty. The limitation note is accurate; its impact is about nil. Saying so would strengthen the report.
- **Simulation count:** production `calculate_backtest` is called with `simulations = 5000` (`PreparePortfolioRiskBacktestInputAsync`), while the research replay uses 10,000. Current risk uses max(sim, 10,000). The order-statistic rate at 5,000 draws is 5.195 / 2.707 / 1.213% (vs 5.188 / 2.703 / 1.197%), a negligible difference, but the report should state it.
- **Confidence levels:** the production backtest only uses 0.95 and 0.99. The 97.5% results are research-only.
- **Refit failures:** production fails the whole Python run on a refit failure (and falls back to C#); research keeps the previous fit. There were none in v3, so this does not affect the conclusions.
- **Return basis:** the replay's realised return `exp(r)ᵀw − Σw` equals the C# `Σ w (P_t/P_{t−1} − 1)` with fixed weights (daily rebalancing to the snapshot). It also matches the new validator. OK.

### F8. MINOR: H_F "mechanism" is largely a restatement of clustering; chi-square p-values ignore dependence
- The realised-vol window t−5..t−1 includes the previous breach days. "High realised/predicted ⇒ more breaches" therefore re-expresses the after-breach result and the Christoffersen result; it does not identify a new mechanism.
- `chi2_contingency` treats days as independent, but breaches are serially dependent (that is the finding), so the p-values are anti-conservative.
- Fix: block-bootstrap the group differences, or regress the hit on lagged hits plus the realised/predicted ratio jointly.
- Numbers: "Q5/Q1 at 95% is 1.6–2.9×"; the JSON gives 1.77–2.92×. The "預測波動五分位 p .16–.86" ("predicted-σ quintile p .16–.86") range omits top10 equal 97.5% (p=.083).

### F9. MINOR: H_B and the DCC recommendation
- The variance-ratio CIs are 90%. The long9 equal lower bound is 1.007, and four cells are tested, so "CI 不含 1" ("CI excludes 1") is fragile.
- MZ slopes (0.73–1.64) are reported without SEs.
- The expected ratio under a correct model with estimated parameters is not computed. E2 suggests estimation alone inflates realised-vs-predicted, but I did not record the ratio.
- The univariate portfolio-level variant, which already captures any correlation dynamics in the portfolio series, is *worse* for equal weight (1.53% at 99%; `p_vs_os` = .037 at 99% and .017 at 97.5% in `long9-univariate.json`, not mentioned in the report). That argues against "cross-asset correlation" as the explanation and weakens recommendation §6.3 (DCC).

### F10. MINOR: report wording and consistency
- §1.1 and §3.1 say "八組" ("eight groups") for a 12-cell table.
- §1.3 "違約集中是唯一穩健的缺陷" ("clustering is the only robust defect") conflicts with §1.4 (variance under-prediction) and with the nearest-rank bias, which is itself a defect.
- `history-v3-es-evt-report.md` §4 and §5.1 bodies still state the retracted conclusions ("頻率加深度的低估一致且顯著", "U_ES 十二項全部拒絕" — under-estimation "consistent and significant", "U_ES rejects in all twelve"); only a banner note was added. Strike or annotate the bodies.
- The `es_backtest.py` module docstring still claims the Monte Carlo null "is exact for the discrete FHS distribution". `test_monte_carlo_calibrated_tests_have_nominal_size_on_discrete_fhs` still "verifies" size with realised returns drawn from the forecast samples, the very fixture the report now says is invalid. Mark it as testing self-consistency only.
- The EVT "not recommended" decision rests on non-significant QL/FZ0 differences. With these sample sizes that is absence of evidence. Acceptable as a decision, but it should not be phrased as "no improvement".

### F11. NOTE: multiple testing
About 12 cells × 6–8 tests × several variants are bolded at p < .05 with no correction. The clustering conclusion survives Holm easily: many p-values sit at the Monte Carlo floor (≈.0005 with 2,000 reps). The single U_ES rejection (p=.040) and several variant-level differences in §3.5 do not. Monte Carlo floors of 1/2001 should be shown as "≤.0005", not as "<.001".

---

## Checked and found correct
- **Retraction of model-own nulls:** model-own U_ES is grossly oversized under a correct finite-bank model (E2/E3: 47–100% rejection at 97.5–99%). The retraction of the Z/U_ES-based "ES under-estimation" conclusions is warranted (I did not re-test Z1/Z2 or McNeil–Frey).
- **Order-statistic computation:** `production_order_statistic_rate` correctly reproduces the nearest-rank atom index, including floating-point `ceil` (e.g. 1−0.99 gives index 100 of 10,000, i.e. the 101st). It matches the simulated known-σ rolling-bank rate (E3/E4: 5.18–5.20 / 2.70–2.71 / 1.18–1.20%).
- **Clustering evidence is properly sized:** C_ES(1) and C_ES(5) under both the exchangeable and model-own nulls have roughly nominal size (2.5–8% at 160 reps, 2.5–12.5% at 40 reps) in E2/E3/E2-long. Christoffersen is nominal at 95% and conservative at 97.5/99%. The violation-clustering conclusion is therefore supported, and the report's recommendation to focus on it is sound.
- **Report vs JSON:** all numbers in var-attribution §3.1, §3.2, §3.3, §3.4 and §3.5 match the committed JSON, apart from the minor items in F8/F10. The variants table matches `long9-{refit5,lookback504,univariate}.json`. The "four Kupiec rejections" in history-v3 match `long9-base`/`top10-base` (`kupiec_p` .005, .006, .026, .019).
- **Research replay parity:** the research loop mirrors `engine.calculate_backtest` (fit cadence `offset % 21`, filtering update, seed derivation, nearest-rank VaR/ES), and `test_collect_forecasts_matches_production_backtest` asserts the parity.
- **C# backtest return basis:** the change to `Σ w (P_t/P_{t−1} − 1)` is correct and consistent with the simulated distribution `Σ w (e^{r} − 1)`. Asset log returns are still `log(P_t/P_{t−1})`. The new Python validator (`models.py`, 1e-8 absolute tolerance) is consistent with this (decimal and double round-trip error ≈1e-16). Current risk uses the same backtest input (`RiskCalculationRunService` → `PreparePortfolioRiskBacktestInputAsync`), so the validator does not break it. The legacy C# `PortfolioRiskCalculator` path still uses weighted log returns with an `exp` drawdown, which is internally consistent.
- **`engine.calculate_current_risk`:** `cumprod(1+r)` drawdown and `log1p` for `dailyLogReturns` are correct for simple-return inputs.
- **Eval export:** it does not bridge gaps (`previousDate` is always the prior calendar entry: 0 violations), and the missing-data difference from production is a single day (G).

## Not examined / limits of this review
- Z1, Z2 and McNeil–Frey sizes were not re-simulated (expensive; already retracted). `fhs_tail.py` beyond its header, and the EVT results, were not examined.
- E2 uses one DGP parameterisation, 2 assets and 40–160 reps. Correct-model rates and dispersion will vary with persistence, ν and dimension, so treat the E2-long p-values in F2 as indicative. A parametric bootstrap at the fitted long9 parameters is the right next step.
- H_B variance ratio and H_F diagnostics were not re-derived under the correct-model simulation.
- The other ~550 files in the PR (UI, agents, auth) were not reviewed. C# changes were tested only through the standalone `Align` harness, not the repo's test suite.
- Weibull "unbiased VaR" coverage under estimated parameters was not simulated (E2 suggests it would also run about 0.1 pp above nominal).
