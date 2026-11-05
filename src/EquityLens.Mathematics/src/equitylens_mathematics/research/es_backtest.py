"""Formal Expected Shortfall backtests for the production VT-GARCH-t joint FHS model.

The production backtest (`engine.calculate_backtest`) only reports VaR coverage
tests. This module replays the identical forecasting loop while retaining each
day's full one-day predictive distribution, which the ES tests below require:

* Acerbi-Szekely (2014) Z1 (tail magnitude given a breach) and Z2 (joint
  frequency and magnitude). P-values come from Monte Carlo draws of the
  realised return from each day's own predictive distribution, so the null is
  exact for the discrete FHS distribution rather than relying on tabulated
  critical values.
* McNeil-Frey (2000) exceedance residual test with an i.i.d. bootstrap.
* Du-Escanciano (2017) unconditional (U_ES) and conditional (C_ES) tests on
  cumulative violations built from probability integral transforms.

Sign convention follows production: returns, VaR and ES are left-tail values,
so VaR and ES are negative and a breach is `actual < VaR`. Every test is
oriented so that a small left-tail p-value means risk is under-estimated.
Parameter-estimation uncertainty is not corrected (Du and Escanciano note this
mainly affects the conditional test); results are evidence, not a regulatory
traffic-light verdict.
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import math
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from scipy.stats import chi2, norm

from ..engine import expected_shortfall, historical_var
from ..garch import VtGarchFit, deterministic_seed, fit_vt_garch, one_day_distribution

REFIT_INTERVAL = 21
DEFAULT_LEVELS = (0.95, 0.975, 0.99)


def seed_for(label: str) -> int:
    return int.from_bytes(hashlib.sha256(label.encode()).digest()[:8], "little")


@dataclass(frozen=True)
class ForecastSet:
    """One-day forecasts aligned to realised portfolio returns."""

    actual: np.ndarray  # (T,)
    samples: np.ndarray  # (T, S) sorted predictive draws, float32
    dates: list[str]

    def var_es(self, confidence: float) -> tuple[np.ndarray, np.ndarray]:
        var = np.array([historical_var(row.astype(np.float64), confidence) for row in self.samples])
        es = np.array([expected_shortfall(row.astype(np.float64), confidence) for row in self.samples])
        return var, es

    def pit(self) -> np.ndarray:
        """Empirical PIT of each realised return under that day's distribution."""
        count = self.samples.shape[1]
        return np.array([
            np.searchsorted(row, value, side="right") / count
            for row, value in zip(self.samples, self.actual)
        ])


def collect_forecasts(
    asset_returns: np.ndarray,
    weights: np.ndarray,
    dates: list[str],
    lookback_days: int,
    simulations: int,
    input_hash: str,
) -> ForecastSet:
    """Replay `engine.calculate_backtest` and keep every predictive distribution.

    Fit cadence, daily variance/residual filtering and seed derivation mirror the
    production loop exactly; `tests/test_es_backtest.py` asserts VaR/ES parity.
    """
    matrix_all = np.asarray(asset_returns, dtype=np.float64)
    weights = np.asarray(weights, dtype=np.float64)
    portfolio = np.exp(matrix_all).T @ weights - weights.sum()
    actual: list[float] = []
    samples: list[np.ndarray] = []
    out_dates: list[str] = []
    active_fit: VtGarchFit | None = None
    for offset, index in enumerate(range(lookback_days, matrix_all.shape[1])):
        if active_fit is None or offset % REFIT_INTERVAL == 0:
            active_fit = fit_vt_garch(matrix_all[:, index - lookback_days:index])
        distribution = one_day_distribution(
            active_fit, weights, simulations,
            deterministic_seed(input_hash, "backtest", offset),
        )
        samples.append(np.sort(distribution).astype(np.float32))
        actual.append(float(portfolio[index]))
        out_dates.append(dates[index])
        current = matrix_all[:, index] * 100.0
        parameters = active_fit.parameters
        standardized = current / np.sqrt(active_fit.next_variances)
        residuals = np.vstack((active_fit.residuals[1:], standardized))
        next_variances = np.asarray([
            item.omega + item.alpha * current[asset] ** 2
            + item.beta * active_fit.next_variances[asset]
            for asset, item in enumerate(parameters)
        ])
        active_fit = VtGarchFit(parameters, residuals, next_variances, active_fit.warning_count)
    return ForecastSet(np.asarray(actual), np.vstack(samples), out_dates)


# --------------------------------------------------------------------------- tests


def z1_statistic(actual: np.ndarray, var: np.ndarray, es: np.ndarray) -> float:
    """Acerbi-Szekely Z1; negative when breach losses exceed predicted ES."""
    breach = actual < var
    if not np.any(breach):
        return float("nan")
    return float(1.0 - np.mean(actual[breach] / es[breach]))


def z2_statistic(actual: np.ndarray, var: np.ndarray, es: np.ndarray, confidence: float) -> float:
    """Acerbi-Szekely Z2; negative when risk (frequency x severity) is under-estimated."""
    tail = 1.0 - confidence
    breach = actual < var
    return float(1.0 - np.sum(np.where(breach, actual / es, 0.0)) / (actual.size * tail))


def acerbi_szekely(
    forecasts: ForecastSet, confidence: float, repetitions: int = 10_000, label: str = ""
) -> dict:
    """Z1/Z2 with exact Monte Carlo nulls drawn from each day's FHS distribution."""
    var, es = forecasts.var_es(confidence)
    actual = forecasts.actual
    z1 = z1_statistic(actual, var, es)
    z2 = z2_statistic(actual, var, es, confidence)
    rng = np.random.default_rng(seed_for(f"acerbi-szekely:{label}:{confidence}"))
    tail = 1.0 - confidence
    count, width = forecasts.samples.shape
    rows = np.arange(count)
    z1_null = np.empty(repetitions)
    z2_null = np.empty(repetitions)
    chunk = 500
    for start in range(0, repetitions, chunk):
        stop = min(start + chunk, repetitions)
        picks = rng.integers(0, width, size=(stop - start, count))
        draws = forecasts.samples[rows[None, :], picks].astype(np.float64)
        breach = draws < var
        ratio = np.where(breach, draws / es, 0.0)
        breaches = breach.sum(axis=1)
        z2_null[start:stop] = 1.0 - ratio.sum(axis=1) / (count * tail)
        with np.errstate(invalid="ignore", divide="ignore"):
            z1_null[start:stop] = np.where(breaches > 0, 1.0 - ratio.sum(axis=1) / breaches, np.nan)
    valid_z1 = z1_null[np.isfinite(z1_null)]
    return {
        "z1": z1,
        "z1_p_left": float((1 + np.sum(valid_z1 <= z1)) / (valid_z1.size + 1)) if math.isfinite(z1) else None,
        "z1_null_mean": float(valid_z1.mean()),
        "z2": z2,
        "z2_p_left": float((1 + np.sum(z2_null <= z2)) / (repetitions + 1)),
        "z2_null_mean": float(z2_null.mean()),
        "z2_critical_5pct": float(np.quantile(z2_null, 0.05)),
        "breaches": int(np.sum(actual < var)),
        "expected_breaches": float(actual.size * tail),
    }


def mcneil_frey(
    forecasts: ForecastSet, confidence: float, repetitions: int = 10_000, label: str = ""
) -> dict:
    """Exceedance residuals (X - ES) / sigma on breach days; H0 mean zero."""
    var, es = forecasts.var_es(confidence)
    sigma = forecasts.samples.astype(np.float64).std(axis=1, ddof=1)
    breach = forecasts.actual < var
    residuals = (forecasts.actual[breach] - es[breach]) / sigma[breach]
    if residuals.size < 2:
        return {"n": int(residuals.size), "mean_residual": None, "p_left": None}
    observed = residuals.mean()
    centred = residuals - observed
    rng = np.random.default_rng(seed_for(f"mcneil-frey:{label}:{confidence}"))
    boot = centred[rng.integers(0, centred.size, size=(repetitions, centred.size))].mean(axis=1)
    return {
        "n": int(residuals.size),
        "mean_residual": float(observed),
        "p_left": float((1 + np.sum(boot <= observed)) / (repetitions + 1)),
    }


def du_escanciano(pit: np.ndarray, confidence: float, lags: tuple[int, ...] = (1, 5)) -> dict:
    """Unconditional (U_ES) and conditional (C_ES) cumulative-violation tests."""
    tail = 1.0 - confidence
    hits = np.where(pit <= tail, (tail - pit) / tail, 0.0)
    count = hits.size
    mean = float(hits.mean())
    z = math.sqrt(count) * (mean - tail / 2) / math.sqrt(tail * (1 / 3 - tail / 4))
    centred = hits - tail / 2
    denominator = float(np.sum(centred**2))
    conditional = {}
    for lag_count in lags:
        rho = [float(np.sum(centred[j:] * centred[:-j]) / denominator) for j in range(1, lag_count + 1)]
        statistic = count * float(np.sum(np.square(rho)))
        conditional[str(lag_count)] = {"statistic": statistic, "p": float(chi2.sf(statistic, lag_count))}
    return {
        "mean_h": mean,
        "expected_h": tail / 2,
        "u_es_z": z,
        "u_es_p_two_sided": float(2 * norm.sf(abs(z))),
        "u_es_p_left": float(norm.sf(z)),
        "c_es": conditional,
    }


def tail_ratio(forecasts: ForecastSet, confidence: float) -> float | None:
    var, es = forecasts.var_es(confidence)
    breach = forecasts.actual < var
    if not np.any(breach):
        return None
    return float(abs(forecasts.actual[breach].mean()) / abs(es[breach].mean()))


def evaluate(forecasts: ForecastSet, levels=DEFAULT_LEVELS, repetitions: int = 10_000, label: str = "") -> dict:
    pit = forecasts.pit()
    return {
        str(level): {
            "acerbi_szekely": acerbi_szekely(forecasts, level, repetitions, label),
            "mcneil_frey": mcneil_frey(forecasts, level, repetitions, label),
            "du_escanciano": du_escanciano(pit, level),
            "tail_ratio": tail_ratio(forecasts, level),
        }
        for level in levels
    }


# --------------------------------------------------------------------------- seed runner


def load_seed_panel(bundle: Path, excluded: set[str]) -> tuple[list[str], list[str], np.ndarray]:
    """Adjusted-close log returns over common dates from a seed bundle (json.gz)."""
    with gzip.open(bundle, "rt", encoding="utf-8") as handle:
        tables = json.load(handle)["tables"]
    tickers = {row["id"]: row["ticker"] for row in tables["security"]}
    series: dict[str, dict[str, float]] = {}
    for row in tables["market_price"]:
        ticker = tickers.get(row["security_id"])
        if row.get("interval") != "1d" or ticker in excluded or ticker is None:
            continue
        value = row.get("adjusted_close")
        if value is not None and value > 0:
            series.setdefault(ticker, {})[row["price_time"][:10]] = float(value)
    names = sorted(series)
    common = sorted(set.intersection(*(set(series[name]) for name in names)))
    prices = np.array([[series[name][day] for day in common] for name in names])
    return names, common[1:], np.diff(np.log(prices), axis=1)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--seed-bundle", type=Path, required=True)
    parser.add_argument("--constituents", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--lookback", type=int, default=252)
    parser.add_argument("--simulations", type=int, default=10_000)
    parser.add_argument("--repetitions", type=int, default=10_000)
    parser.add_argument("--exclude", default="0050,7769")
    args = parser.parse_args()

    names, dates, returns = load_seed_panel(args.seed_bundle, set(args.exclude.split(",")))
    published = {item["code"]: item["weights"] for item in json.loads(args.constituents.read_text())["constituents"]}
    cap = np.array([published.get(name, 0.0) for name in names])
    portfolios = {"equal": np.full(len(names), 1 / len(names)), "cap": cap / cap.sum()}
    args.output.mkdir(parents=True, exist_ok=True)
    summary = {
        "model": "VT-GARCH-t + Joint-Vector FHS (production replay)",
        "assets": names, "lookback": args.lookback, "simulations": args.simulations,
        "repetitions": args.repetitions, "refit_interval": REFIT_INTERVAL,
        "levels": list(DEFAULT_LEVELS), "portfolios": {},
    }
    for label, weights in portfolios.items():
        forecasts = collect_forecasts(
            returns, weights, dates, args.lookback, args.simulations, f"es-backtest:{label}"
        )
        result = evaluate(forecasts, DEFAULT_LEVELS, args.repetitions, label)
        summary["portfolios"][label] = {
            "hhi": float(np.sum(weights**2)), "observations": int(forecasts.actual.size),
            "from": forecasts.dates[0], "to": forecasts.dates[-1], "tests": result,
        }
        pit = forecasts.pit()
        lines = ["date,actual,pit," + ",".join(f"var{lv},es{lv}" for lv in DEFAULT_LEVELS)]
        pairs = [forecasts.var_es(level) for level in DEFAULT_LEVELS]
        for t, day in enumerate(forecasts.dates):
            cells = [day, f"{forecasts.actual[t]:.8f}", f"{pit[t]:.6f}"]
            for var, es in pairs:
                cells += [f"{var[t]:.8f}", f"{es[t]:.8f}"]
            lines.append(",".join(cells))
        (args.output / f"daily_{label}.csv").write_text("\n".join(lines) + "\n")
        print(label, json.dumps(result), flush=True)
    (args.output / "es_backtest_summary.json").write_text(
        json.dumps(summary, indent=1, ensure_ascii=False) + "\n"
    )


if __name__ == "__main__":
    main()
