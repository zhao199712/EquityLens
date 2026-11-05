"""Attribution of VaR under-coverage and violation clustering on long histories.

Pre-specified hypotheses (see docs/risk/validation/var-attribution-report.md):

H_A finite-sample quantile bias - production takes a nearest-rank quantile of a
    distribution with only `lookback` distinct scenarios; even under a correct
    model a new return falls below the j-th worst of n exchangeable scenarios
    with probability j / (n + 1). Tested against the exact breach rate implied
    by the production nearest-rank rule, and by recomputing VaR with an
    unbiased (Weibull plotting position) quantile of the same scenarios.
H_B variance level - realised squared returns versus predicted variance.
H_C parameter staleness - breach rate by days since refit; refit every 5 days.
H_D residual-bank length - lookback 504 instead of 252.
H_E cross-sectional structure - univariate VT-GARCH-FHS on the portfolio series.
H_F clustering mechanism - breach rate after a breach and by the ratio of
    recent realised volatility to predicted volatility.

Each variant changes one component; everything else follows production.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import numpy as np
from scipy.stats import binom, chi2_contingency

from .additional_validation import christoffersen, kupiec
from .es_backtest import (
    DEFAULT_LEVELS,
    ForecastSet,
    collect_forecasts_multi,
    du_escanciano_mc,
    load_history_export,
    seed_for,
)

VARIANTS = ("base", "refit5", "lookback504", "univariate")


def production_order_statistic_rate(
    bank_size: int, confidence: float, simulations: int = 10_000, repetitions: int = 20_000
) -> float:
    """Exact breach probability of the production nearest-rank VaR under a correct model.

    The one-day distribution resamples `bank_size` exchangeable scenarios
    `simulations` times; nearest-rank picks the atom whose cumulative count
    first reaches ceil((1 - confidence) * simulations), computed exactly as
    `engine.nearest_rank` does (including floating-point rounding). A new
    exchangeable return falls strictly below the j-th smallest atom with
    probability j / (bank_size + 1), independent of the distribution.
    """
    rng = np.random.default_rng(seed_for(f"order-statistic:{bank_size}:{confidence}"))
    target = max(0, min(math.ceil((1.0 - confidence) * simulations) - 1, simulations - 1)) + 1
    counts = rng.multinomial(simulations, np.full(bank_size, 1.0 / bank_size), size=repetitions)
    atom = np.argmax(np.cumsum(counts, axis=1) >= target, axis=1) + 1
    return float(np.mean(atom / (bank_size + 1)))


def unbiased_var(forecasts: ForecastSet, confidence: float) -> np.ndarray:
    """VaR from each day's distinct scenarios with a Weibull (p(n+1)) plotting position."""
    tail = 1.0 - confidence
    return np.array([
        np.quantile(np.unique(row.astype(np.float64)), tail, method="weibull")
        for row in forecasts.samples
    ])


def binomial_two_sided(breaches: int, count: int, rate: float) -> float:
    lower = binom.cdf(breaches, count, rate)
    upper = binom.sf(breaches - 1, count, rate)
    return float(min(1.0, 2 * min(lower, upper)))


def coverage(actual: np.ndarray, var: np.ndarray, confidence: float, adjusted_rate: float | None) -> dict:
    breaches = actual < var
    count = int(breaches.sum())
    result = {
        "breaches": count,
        "rate": float(breaches.mean()),
        "nominal_rate": 1.0 - confidence,
        "kupiec_p": kupiec(breaches, 1.0 - confidence),
        "christoffersen_p": christoffersen(breaches),
    }
    if adjusted_rate is not None:
        result["order_statistic_rate"] = adjusted_rate
        result["p_vs_order_statistic"] = binomial_two_sided(count, breaches.size, adjusted_rate)
    return result


def block_bootstrap_ratio(numerator: np.ndarray, denominator: np.ndarray, label: str,
                          block: int = 21, repetitions: int = 2_000) -> dict:
    rng = np.random.default_rng(seed_for(label))
    count = numerator.size
    blocks = math.ceil(count / block)
    estimates = np.empty(repetitions)
    for repetition in range(repetitions):
        starts = rng.integers(0, count, size=blocks)
        index = ((starts[:, None] + np.arange(block)) % count).ravel()[:count]
        estimates[repetition] = numerator[index].mean() / denominator[index].mean()
    return {
        "ratio": float(numerator.mean() / denominator.mean()),
        "ci90": np.quantile(estimates, [0.05, 0.95]).tolist(),
    }


def rate_by_group(breaches: np.ndarray, groups: np.ndarray, labels: list[str]) -> dict:
    table = np.array([[np.sum(breaches & (groups == g)), np.sum(~breaches & (groups == g))]
                      for g in range(len(labels))])
    statistic, p_value, _, _ = chi2_contingency(table)
    return {
        "groups": {label: {"days": int(row.sum()), "rate": float(row[0] / row.sum())}
                   for label, row in zip(labels, table)},
        "chi2_p": float(p_value),
    }


def diagnostics(forecasts: ForecastSet, refit_interval: int, label: str) -> dict:
    """Mechanism diagnostics on the production replay (H_B, H_C, H_F)."""
    actual = forecasts.actual
    sigma = forecasts.samples.astype(np.float64).std(axis=1, ddof=1)
    result: dict = {
        "variance_ratio": block_bootstrap_ratio(actual**2, sigma**2, f"variance:{label}"),
    }
    design = np.column_stack((np.ones_like(sigma), sigma**2))
    coefficients, *_ = np.linalg.lstsq(design, actual**2, rcond=None)
    result["mincer_zarnowitz"] = {"intercept": float(coefficients[0]), "slope": float(coefficients[1])}

    offset = np.arange(actual.size) % refit_interval
    staleness = np.minimum(offset // 7, 2)
    realised = np.full(actual.size, np.nan)
    for t in range(5, actual.size):
        realised[t] = math.sqrt(float(np.mean(actual[t - 5:t] ** 2)))
    surprise = realised / sigma
    valid = np.isfinite(surprise)
    edges = np.quantile(surprise[valid], [0.2, 0.4, 0.6, 0.8])
    surprise_group = np.digitize(surprise, edges)
    sigma_group = np.digitize(sigma, np.quantile(sigma, [0.2, 0.4, 0.6, 0.8]))
    for confidence in DEFAULT_LEVELS:
        var, _ = forecasts.var_es(confidence)
        breaches = actual < var
        previous = breaches[:-1]
        result[str(confidence)] = {
            "days_since_refit": rate_by_group(breaches, staleness, ["0-6", "7-13", "14-20"]),
            "after_breach": {
                "rate_after_breach": float(breaches[1:][previous].mean()) if previous.any() else None,
                "rate_after_no_breach": float(breaches[1:][~previous].mean()),
            },
            "recent_realised_over_predicted_quintile": rate_by_group(
                breaches[valid], surprise_group[valid], ["Q1 low", "Q2", "Q3", "Q4", "Q5 high"]),
            "predicted_sigma_quintile": rate_by_group(
                breaches, sigma_group, ["Q1 low", "Q2", "Q3", "Q4", "Q5 high"]),
            "surprise_quintile_edges": edges.tolist(),
        }
    return result


def portfolio_log_returns(returns: np.ndarray, weights: np.ndarray) -> np.ndarray:
    return np.log1p(np.expm1(returns).T @ weights)[None, :]


def run_variant(variant: str, history: Path, name: str, repetitions: int) -> dict:
    tickers, dates, returns, snapshot = load_history_export(history, name)
    weights = {"snapshot": snapshot, "equal": np.full(len(tickers), 1 / len(tickers))}
    lookback = 504 if variant == "lookback504" else 252
    refit = 5 if variant == "refit5" else 21
    if variant == "univariate":
        forecasts = {
            label: collect_forecasts_multi(
                portfolio_log_returns(returns, w), {label: (np.array([1.0]), f"es-backtest:{name}-{label}")},
                dates, lookback, 10_000, keep_previous_on_fit_failure=True)[label]
            for label, w in weights.items()
        }
    else:
        forecasts = collect_forecasts_multi(
            returns, {label: (w, f"es-backtest:{name}-{label}") for label, w in weights.items()},
            dates, lookback, 10_000, keep_previous_on_fit_failure=True, refit_interval=refit)
    output: dict = {"variant": variant, "lookback": lookback, "refit_interval": refit, "portfolios": {}}
    for label, fc in forecasts.items():
        entry: dict = {"observations": int(fc.actual.size), "from": fc.dates[0], "to": fc.dates[-1],
                       "refit_failures_kept_previous": fc.fit_failures, "levels": {}}
        for confidence in DEFAULT_LEVELS:
            var, _ = fc.var_es(confidence)
            adjusted = production_order_statistic_rate(lookback, confidence)
            level = {"production_var": coverage(fc.actual, var, confidence, adjusted)}
            de = du_escanciano_mc(fc, confidence, repetitions, f"attr:{variant}:{label}")
            level["c_es"] = {lag: value["p"] for lag, value in de["c_es"].items()}
            if variant == "base":
                level["unbiased_var"] = coverage(fc.actual, unbiased_var(fc, confidence), confidence, 1.0 - confidence)
            entry["levels"][str(confidence)] = level
        if variant == "base":
            entry["diagnostics"] = diagnostics(fc, refit, f"{variant}:{label}")
        output["portfolios"][label] = entry
        print(variant, label, "done", flush=True)
    return output


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--history-export", type=Path, required=True)
    parser.add_argument("--portfolio", default="long9")
    parser.add_argument("--variant", choices=VARIANTS, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--repetitions", type=int, default=2_000)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    result = run_variant(args.variant, args.history_export, args.portfolio, args.repetitions)
    (args.output / f"{args.portfolio}-{args.variant}.json").write_text(json.dumps(result, indent=1) + "\n")


if __name__ == "__main__":
    main()
