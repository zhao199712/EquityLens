"""Research challenger: joint-vector FHS with GPD-extended lower tails.

Production FHS can never simulate a standardised shock worse than the worst
residual in its 252-day bank, so crash days fall outside the predictive support
(see docs/risk/validation/es-backtest-report.md). This challenger keeps the
production sampler's empirical copula — it draws exactly the same residual rows
from the same seed — and only replaces each asset's lower-tail marginal:

* per asset, the k = floor(tail_prob * T) smallest residuals form the tail and
  the next order statistic is the threshold u_i;
* a single GPD shape xi is fitted to the pooled threshold exceedances of all
  assets (one year gives each asset only ~25 tail points), clipped to
  [0, XI_MAX]; each asset's scale is beta_i = mean_excess_i * (1 - xi), which
  preserves its empirical mean excess;
* a sampled residual with tail rank q (0 = most extreme) is redrawn from the
  GPD conditional survival band ((q + V) / k), V ~ U(0, 1).

Body values and cross-sectional ranks are untouched, so differences against
production isolate the tail extension. tail_prob = 0.10 is the pre-specified
primary setting; 0.05 is a sensitivity run only.
"""

from __future__ import annotations

import argparse
import json
import math
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from scipy.stats import genpareto

from ..garch import GarchFitError, VtGarchFit
from .additional_validation import christoffersen, fz0_loss, kupiec, paired_test, qloss
from .es_backtest import DEFAULT_LEVELS, collect_forecasts, evaluate, load_seed_panel

XI_MAX = 0.5
MIN_TAIL_POINTS = 5


@dataclass(frozen=True)
class LowerTailModel:
    tail_count: int
    thresholds: np.ndarray  # (N,)
    scales: np.ndarray  # (N,)
    xi: float
    ranks: np.ndarray  # (T, N) 0-based rank of each residual within its asset


def fit_lower_tails(residuals: np.ndarray, tail_prob: float) -> LowerTailModel:
    residuals = np.asarray(residuals, dtype=np.float64)
    count, assets = residuals.shape
    tail_count = max(int(math.floor(tail_prob * count)), MIN_TAIL_POINTS)
    if tail_count >= count:
        raise GarchFitError("tail extension needs more residuals than tail points")
    ordered = np.sort(residuals, axis=0)
    thresholds = ordered[tail_count]
    excess = thresholds[None, :] - ordered[:tail_count]
    pooled = excess[excess > 0]
    try:
        xi, _, _ = genpareto.fit(pooled, floc=0)
        xi = float(xi) if math.isfinite(xi) else 0.0
    except (ValueError, RuntimeError):
        xi = 0.0
    xi = min(max(xi, 0.0), XI_MAX)
    mean_excess = np.maximum(excess.mean(axis=0), 1e-6)
    ranks = np.argsort(np.argsort(residuals, axis=0, kind="stable"), axis=0, kind="stable")
    return LowerTailModel(tail_count, thresholds, mean_excess * (1.0 - xi), xi, ranks)


def gpd_excess(survival: np.ndarray, scale: np.ndarray, xi: float) -> np.ndarray:
    if xi < 1e-9:
        return -scale * np.log(survival)
    return scale / xi * (survival ** (-xi) - 1.0)


def one_day_distribution_evt(
    fit: VtGarchFit,
    weights: np.ndarray,
    simulations: int,
    seed: int,
    tail_prob: float = 0.10,
    diagnostics: list | None = None,
) -> np.ndarray:
    """Production one-day sampler with GPD-extended lower tails (same row draws)."""
    weights = np.asarray(weights, dtype=np.float64)
    rng = np.random.default_rng(seed)
    rows = rng.integers(0, fit.residuals.shape[0], size=simulations)
    model = fit_lower_tails(fit.residuals, tail_prob)
    sampled = fit.residuals[rows]
    ranks = model.ranks[rows]
    in_tail = ranks < model.tail_count
    jitter = np.random.default_rng([seed, 1]).random(sampled.shape)
    survival = np.clip((ranks + jitter) / model.tail_count, 1e-12, 1.0)
    extended = model.thresholds[None, :] - gpd_excess(survival, model.scales[None, :], model.xi)
    shocks = np.where(in_tail, extended, sampled)
    simulated = shocks * (np.sqrt(fit.next_variances) / 100.0)
    distribution = 1.0 - weights.sum() + np.exp(simulated) @ weights - 1.0
    if np.any(~np.isfinite(distribution)):
        raise GarchFitError("tail-extended distribution contains non-finite values")
    if diagnostics is not None:
        diagnostics.append(model.xi)
    return distribution


def var_tests(forecasts, confidence: float) -> dict:
    var, es = forecasts.var_es(confidence)
    breaches = forecasts.actual < var
    return {
        "breaches": int(breaches.sum()),
        "kupiec_p": kupiec(breaches, 1.0 - confidence),
        "christoffersen_p": christoffersen(breaches),
        "mean_ql": float(qloss(forecasts.actual, var, confidence).mean()),
        "mean_fz0": float(fz0_loss(forecasts.actual, var, es, confidence).mean()),
    }


def compare(base, challenger, confidence: float, label: str) -> dict:
    base_var, base_es = base.var_es(confidence)
    var, es = challenger.var_es(confidence)
    actual = base.actual
    return {
        "ql": paired_test(qloss(actual, var, confidence) - qloss(actual, base_var, confidence), f"ql:{label}:{confidence}"),
        "fz0": paired_test(
            fz0_loss(actual, var, es, confidence) - fz0_loss(actual, base_var, base_es, confidence),
            f"fz0:{label}:{confidence}",
        ),
        "mean_var_ratio": float(np.mean(var / base_var)),
        "mean_es_ratio": float(np.mean(es / base_es)),
        "support_misses": {"base": int(np.sum(base.pit() == 0)), "challenger": int(np.sum(challenger.pit() == 0))},
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--seed-bundle", type=Path, required=True)
    parser.add_argument("--constituents", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--tail-probs", default="0.10,0.05")
    parser.add_argument("--repetitions", type=int, default=10_000)
    args = parser.parse_args()

    names, dates, returns = load_seed_panel(args.seed_bundle, {"0050", "7769"})
    published = {item["code"]: item["weights"] for item in json.loads(args.constituents.read_text())["constituents"]}
    cap = np.array([published.get(name, 0.0) for name in names])
    portfolios = {"equal": np.full(len(names), 1 / len(names)), "cap": cap / cap.sum()}
    tail_probs = [float(item) for item in args.tail_probs.split(",")]
    summary: dict = {"primary_tail_prob": tail_probs[0], "xi_max": XI_MAX, "portfolios": {}}
    args.output.mkdir(parents=True, exist_ok=True)
    for label, weights in portfolios.items():
        common = dict(asset_returns=returns, weights=weights, dates=dates, lookback_days=252,
                      simulations=10_000, input_hash=f"es-backtest:{label}")
        base = collect_forecasts(**common)
        entry = {"production": {
            "es_tests": evaluate(base, DEFAULT_LEVELS, args.repetitions, f"{label}:production"),
            "var_tests": {str(lv): var_tests(base, lv) for lv in DEFAULT_LEVELS},
        }}
        for tail_prob in tail_probs:
            xi_seen: list[float] = []

            def sampler(fit, w, sims, seed, tail_prob=tail_prob, xi_seen=xi_seen):
                return one_day_distribution_evt(fit, w, sims, seed, tail_prob, xi_seen)

            challenger = collect_forecasts(**common, distribution=sampler)
            name = f"evt_{tail_prob:.2f}"
            entry[name] = {
                "xi_quantiles_10_50_90": np.quantile(xi_seen, [0.1, 0.5, 0.9]).tolist(),
                "xi_at_cap_share": float(np.mean(np.asarray(xi_seen) >= XI_MAX - 1e-9)),
                "es_tests": evaluate(challenger, DEFAULT_LEVELS, args.repetitions, f"{label}:{name}"),
                "var_tests": {str(lv): var_tests(challenger, lv) for lv in DEFAULT_LEVELS},
                "vs_production": {str(lv): compare(base, challenger, lv, f"{label}:{name}") for lv in DEFAULT_LEVELS},
            }
            print(label, name, "done", flush=True)
        summary["portfolios"][label] = entry
    (args.output / "fhs_evt_tail_summary.json").write_text(json.dumps(summary, indent=1) + "\n")
    print("DONE", flush=True)


if __name__ == "__main__":
    main()
