"""Formal Expected Shortfall backtests for the production VT-GARCH-t joint FHS model.

The production backtest (`engine.calculate_backtest`) only reports VaR coverage
tests. This module replays the identical forecasting loop while retaining each
day's full one-day predictive distribution, which the ES tests below require:

* Acerbi-Szekely (2014) Z1 (tail magnitude given a breach) and Z2 (joint
  frequency and magnitude). P-values come from Monte Carlo draws of the
  realised return from each day's own predictive distribution, so the null is
  exact for the discrete FHS distribution rather than relying on tabulated
  critical values.
* McNeil-Frey (2000) exceedance residuals, with a Monte Carlo null from the
  model's own distributions (the discrete FHS boundary makes a zero-mean null
  invalid).
* Du-Escanciano (2017) unconditional (U_ES) and conditional (C_ES) tests on
  cumulative violations built from probability integral transforms, also with
  Monte Carlo nulls; asymptotic chi-square p-values are reported for reference
  only because they are oversized at this sample size.

Sign convention follows production: returns, VaR and ES are left-tail values,
so VaR and ES are negative and a breach is `actual < VaR`. Every one-sided
p-value is oriented so that a small value means risk is under-estimated
(left tail for Z1/Z2/McNeil-Frey, right tail for U_ES/C_ES).
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
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

import numpy as np
from scipy.stats import chi2, norm

from ..engine import expected_shortfall, historical_var
from ..garch import GarchFitError, VtGarchFit, deterministic_seed, fit_vt_garch, one_day_distribution

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
    fit_failures: list[str] = field(default_factory=list)  # refit dates that kept the previous fit

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
    distribution: Callable[[VtGarchFit, np.ndarray, int, int], np.ndarray] = one_day_distribution,
    keep_previous_on_fit_failure: bool = False,
) -> ForecastSet:
    """Replay `engine.calculate_backtest` and keep every predictive distribution.

    Fit cadence, daily variance/residual filtering and seed derivation mirror the
    production loop exactly; `tests/test_es_backtest.py` asserts VaR/ES parity.
    `distribution` lets research challengers replace only the one-day sampler.
    Production fails the whole run when a refit fails (and falls back to the C#
    engine); `keep_previous_on_fit_failure` instead keeps filtering with the
    previous parameters and records the date, so long histories stay comparable.
    """
    return collect_forecasts_multi(
        asset_returns, {"": (weights, input_hash)}, dates, lookback_days, simulations,
        distribution, keep_previous_on_fit_failure,
    )[""]


def collect_forecasts_multi(
    asset_returns: np.ndarray,
    portfolios: dict[str, tuple[np.ndarray, str]],
    dates: list[str],
    lookback_days: int,
    simulations: int,
    distribution: Callable[[VtGarchFit, np.ndarray, int, int], np.ndarray] = one_day_distribution,
    keep_previous_on_fit_failure: bool = False,
    refit_interval: int = REFIT_INTERVAL,
) -> dict[str, ForecastSet]:
    """`collect_forecasts` for several weight vectors sharing one sequence of fits.

    `portfolios` maps label -> (weights, input_hash); each label keeps its own
    seed derivation, so results equal separate `collect_forecasts` calls.
    `refit_interval` exists for research attribution; production uses 21.
    """
    matrix_all = np.asarray(asset_returns, dtype=np.float64)
    prepared = {
        label: (np.asarray(weights, dtype=np.float64), input_hash)
        for label, (weights, input_hash) in portfolios.items()
    }
    portfolio_returns = {
        label: np.exp(matrix_all).T @ weights - weights.sum()
        for label, (weights, _) in prepared.items()
    }
    actual: dict[str, list[float]] = {label: [] for label in prepared}
    samples: dict[str, list[np.ndarray]] = {label: [] for label in prepared}
    out_dates: list[str] = []
    failures: list[str] = []
    active_fit: VtGarchFit | None = None
    for offset, index in enumerate(range(lookback_days, matrix_all.shape[1])):
        if active_fit is None or offset % refit_interval == 0:
            try:
                active_fit = fit_vt_garch(matrix_all[:, index - lookback_days:index])
            except GarchFitError:
                if active_fit is None or not keep_previous_on_fit_failure:
                    raise
                failures.append(dates[index])
        for label, (weights, input_hash) in prepared.items():
            draws = distribution(
                active_fit, weights, simulations,
                deterministic_seed(input_hash, "backtest", offset),
            )
            samples[label].append(np.sort(draws).astype(np.float32))
            actual[label].append(float(portfolio_returns[label][index]))
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
    return {
        label: ForecastSet(np.asarray(actual[label]), np.vstack(samples[label]), list(out_dates), list(failures))
        for label in prepared
    }


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


def right_ranks(forecasts: ForecastSet) -> np.ndarray:
    """For each day and sample position, the count of samples <= that sample (PIT numerator)."""
    return np.vstack([np.searchsorted(row, row, side="right") for row in forecasts.samples]).astype(np.int32)


def _null_draws(forecasts: ForecastSet, repetitions: int, label: str, chunk: int = 250):
    """Yield realised-return draws from each day's own predictive distribution, with sample positions."""
    rng = np.random.default_rng(seed_for(label))
    count, width = forecasts.samples.shape
    rows = np.arange(count)
    for start in range(0, repetitions, chunk):
        picks = rng.integers(0, width, size=(min(chunk, repetitions - start), count))
        yield forecasts.samples[rows[None, :], picks].astype(np.float64), picks


def _left_p(null: np.ndarray, observed: float) -> float | None:
    valid = null[np.isfinite(null)]
    if not math.isfinite(observed) or valid.size == 0:
        return None
    return float((1 + np.sum(valid <= observed)) / (valid.size + 1))


def _right_p(null: np.ndarray, observed: float) -> float | None:
    valid = null[np.isfinite(null)]
    if not math.isfinite(observed) or valid.size == 0:
        return None
    return float((1 + np.sum(valid >= observed)) / (valid.size + 1))


def _mcneil_frey_stat(values: np.ndarray, var: np.ndarray, es: np.ndarray, sigma: np.ndarray) -> np.ndarray:
    breach = values < var
    residual = np.where(breach, (values - es) / sigma, 0.0)
    with np.errstate(invalid="ignore", divide="ignore"):
        return residual.sum(axis=-1) / breach.sum(axis=-1)


def mcneil_frey(
    forecasts: ForecastSet, confidence: float, repetitions: int = 10_000, label: str = ""
) -> dict:
    """Mean exceedance residual (X - ES) / sigma on breach days.

    The discrete FHS distribution with a nearest-rank VaR does not give the
    residual a zero mean under a correct model, so the null is simulated from
    each day's own predictive distribution instead of bootstrapping around 0.
    """
    var, es = forecasts.var_es(confidence)
    sigma = forecasts.samples.astype(np.float64).std(axis=1, ddof=1)
    observed = float(_mcneil_frey_stat(forecasts.actual, var, es, sigma))
    null = np.concatenate([
        _mcneil_frey_stat(draws, var, es, sigma)
        for draws, _ in _null_draws(forecasts, repetitions, f"mcneil-frey:{label}:{confidence}")
    ])
    finite = null[np.isfinite(null)]
    return {
        "n": int(np.sum(forecasts.actual < var)),
        "mean_residual": observed if math.isfinite(observed) else None,
        "null_mean": float(finite.mean()) if finite.size else None,
        "p_left": _left_p(null, observed),
    }


def _du_escanciano_stats(pit: np.ndarray, tail: float, lags: tuple[int, ...]) -> tuple[np.ndarray, dict]:
    hits = np.where(pit <= tail, (tail - pit) / tail, 0.0)
    centred = hits - tail / 2
    denominator = np.sum(centred**2, axis=-1)
    count = hits.shape[-1]
    conditional = {}
    for lag_count in lags:
        total = np.zeros(hits.shape[:-1])
        for lag in range(1, lag_count + 1):
            with np.errstate(invalid="ignore", divide="ignore"):
                rho = np.sum(centred[..., lag:] * centred[..., :-lag], axis=-1) / denominator
            total = total + rho**2
        conditional[lag_count] = count * total
    return hits.mean(axis=-1), conditional


def du_escanciano(pit: np.ndarray, confidence: float, lags: tuple[int, ...] = (1, 5)) -> dict:
    """Asymptotic U_ES / C_ES (reference only; oversized at T~1,500 for 99% and lag 5)."""
    tail = 1.0 - confidence
    mean, conditional = _du_escanciano_stats(np.asarray(pit, dtype=np.float64), tail, lags)
    count = len(pit)
    z = math.sqrt(count) * (float(mean) - tail / 2) / math.sqrt(tail * (1 / 3 - tail / 4))
    return {
        "mean_h": float(mean),
        "expected_h": tail / 2,
        "u_es_z": z,
        "u_es_p_two_sided": float(2 * norm.sf(abs(z))),
        "u_es_p_left": float(norm.sf(z)),
        "c_es": {
            str(lag): {"statistic": float(value), "p": float(chi2.sf(float(value), lag))}
            for lag, value in conditional.items()
        },
    }


def du_escanciano_mc(
    forecasts: ForecastSet,
    confidence: float,
    repetitions: int = 10_000,
    label: str = "",
    ranks: np.ndarray | None = None,
    lags: tuple[int, ...] = (1, 5),
) -> dict:
    """U_ES / C_ES with nulls from PITs of draws from each day's own distribution."""
    tail = 1.0 - confidence
    ranks = right_ranks(forecasts) if ranks is None else ranks
    width = forecasts.samples.shape[1]
    rows = np.arange(forecasts.samples.shape[0])
    observed_mean, observed_conditional = _du_escanciano_stats(forecasts.pit(), tail, lags)
    null_mean: list[np.ndarray] = []
    null_conditional: dict[int, list[np.ndarray]] = {lag: [] for lag in lags}
    for _, picks in _null_draws(forecasts, repetitions, f"du-escanciano:{label}:{confidence}"):
        pit = ranks[rows[None, :], picks] / width
        mean, conditional = _du_escanciano_stats(pit, tail, lags)
        null_mean.append(mean)
        for lag in lags:
            null_conditional[lag].append(conditional[lag])
    means = np.concatenate(null_mean)
    return {
        "mean_h": float(observed_mean),
        "null_mean_h": float(means.mean()),
        "u_es_p_right": _right_p(means, float(observed_mean)),
        "c_es": {
            str(lag): {
                "statistic": float(observed_conditional[lag]),
                "p": _right_p(np.concatenate(null_conditional[lag]), float(observed_conditional[lag])),
            }
            for lag in lags
        },
    }


def exchangeable_cumulative_pits(forecasts: ForecastSet) -> np.ndarray:
    """Per day, PIT values a new return can take when it is exchangeable with the bank.

    Row t holds 0, c_1/S, ..., c_n/S where c_k counts samples at or below the
    k-th distinct scenario. Under a correct finite-bank FHS model a new return
    falls into each of the n + 1 gaps around the n scenarios with equal
    probability, whatever the true distribution, so these values are equally
    likely PITs. Days with fewer distinct values are padded by repeating the
    last entry, which is only drawn with the padded probability mass removed.
    """
    rows = []
    for row in forecasts.samples:
        _, counts = np.unique(row, return_counts=True)
        rows.append(np.concatenate(([0.0], np.cumsum(counts) / row.size)))
    width = max(len(row) for row in rows)
    table = np.full((len(rows), width), np.nan)
    for index, row in enumerate(rows):
        table[index, :len(row)] = row
    return table


def du_escanciano_exchangeable(
    forecasts: ForecastSet,
    confidence: float,
    repetitions: int = 10_000,
    label: str = "",
    lags: tuple[int, ...] = (1, 5),
) -> dict:
    """U_ES / C_ES with a distribution-free null that accounts for the finite bank.

    The model-own null in `du_escanciano_mc` treats the resampled bank as the
    true distribution and rejects correct finite-bank models; here the new
    return's rank among the n scenarios is uniform on n + 1 positions.
    """
    tail = 1.0 - confidence
    table = exchangeable_cumulative_pits(forecasts)
    sizes = np.sum(np.isfinite(table), axis=1)
    observed_mean, observed_conditional = _du_escanciano_stats(forecasts.pit(), tail, lags)
    rng = np.random.default_rng(seed_for(f"du-escanciano-exchangeable:{label}:{confidence}"))
    rows = np.arange(table.shape[0])
    means: list[np.ndarray] = []
    conditional: dict[int, list[np.ndarray]] = {lag: [] for lag in lags}
    for start in range(0, repetitions, 250):
        count = min(250, repetitions - start)
        positions = np.floor(rng.random((count, rows.size)) * sizes).astype(int)
        pit = table[rows[None, :], positions]
        mean, stats = _du_escanciano_stats(pit, tail, lags)
        means.append(mean)
        for lag in lags:
            conditional[lag].append(stats[lag])
    null_means = np.concatenate(means)
    return {
        "mean_h": float(observed_mean),
        "null_mean_h": float(null_means.mean()),
        "u_es_p_right": _right_p(null_means, float(observed_mean)),
        "c_es": {
            str(lag): {
                "statistic": float(observed_conditional[lag]),
                "p": _right_p(np.concatenate(conditional[lag]), float(observed_conditional[lag])),
            }
            for lag in lags
        },
    }


def tail_ratio(forecasts: ForecastSet, confidence: float) -> float | None:
    var, es = forecasts.var_es(confidence)
    breach = forecasts.actual < var
    if not np.any(breach):
        return None
    return float(abs(forecasts.actual[breach].mean()) / abs(es[breach].mean()))


def evaluate(forecasts: ForecastSet, levels=DEFAULT_LEVELS, repetitions: int = 10_000, label: str = "") -> dict:
    """Monte Carlo nulls from the model's own daily distributions condition on the
    finite resampled bank being the true distribution; for FHS this rejects
    correct models (Z1, Z2 and model-own U_ES are oversized at long T). Use
    `du_escanciano_exchangeable` for frequency/clustering evidence; the
    asymptotic Du-Escanciano block is reported for reference only."""
    pit = forecasts.pit()
    ranks = right_ranks(forecasts)
    return {
        str(level): {
            "acerbi_szekely": acerbi_szekely(forecasts, level, repetitions, label),
            "mcneil_frey": mcneil_frey(forecasts, level, repetitions, label),
            "du_escanciano": du_escanciano_mc(forecasts, level, repetitions, label, ranks),
            "du_escanciano_exchangeable": du_escanciano_exchangeable(forecasts, level, repetitions, label),
            "du_escanciano_asymptotic": du_escanciano(pit, level),
            "tail_ratio": tail_ratio(forecasts, level),
        }
        for level in levels
    }


# --------------------------------------------------------------------------- seed runner


def align_carry_forward(series: dict[str, dict[str, float]]) -> tuple[list[str], list[str], np.ndarray, dict[str, int]]:
    """Mirror C# RiskPriceAlignment: union of dates inside the common coverage
    window, carrying the previous valid price over suspended/missing days."""
    names = sorted(series)
    start = max(min(series[name]) for name in names)
    end = min(max(series[name]) for name in names)
    dates = sorted({day for name in names for day in series[name] if start <= day <= end})
    prices = np.empty((len(names), len(dates)))
    filled: dict[str, int] = {}
    for row, name in enumerate(names):
        earlier = [day for day in series[name] if day < start]
        # An asset may have history before `start` but no row on `start` itself.
        previous = series[name][max(earlier)] if earlier else None
        count = 0
        for column, day in enumerate(dates):
            value = series[name].get(day)
            if value is None:
                value = previous
                count += 1
            prices[row, column] = value
            previous = value
        filled[name] = count
    return names, dates, prices, filled


def load_seed_panel(bundle: Path, excluded: set[str]) -> tuple[list[str], list[str], np.ndarray]:
    """Adjusted-close log returns from a seed bundle (json.gz), aligned like production."""
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
    names, dates, prices, _ = align_carry_forward(series)
    return names, dates[1:], np.diff(np.log(prices), axis=1)


def load_history_export(directory: Path, name: str) -> tuple[list[str], list[str], np.ndarray, np.ndarray]:
    """Read `evals/equitylens_eval/history_seed.py` exports for one portfolio.

    Rows only exist for adjacent sessions where every asset has an accepted
    price; missing or quarantined prices drop both adjacent returns rather than
    being filled, so no row spans more than one session.
    """
    rows = json.loads((directory / f"{name}-returns.json").read_text())
    universe = json.loads((directory / f"{name}-universe.json").read_text())
    simple = np.array([row["returns"] for row in rows], dtype=np.float64).T
    if np.any(simple <= -1.0) or not np.all(np.isfinite(simple)):
        raise ValueError(f"{name} export contains invalid simple returns")
    weights = np.asarray(universe["weights"], dtype=np.float64)
    return list(universe["tickers"]), [row["date"] for row in rows], np.log1p(simple), weights


def add_portfolio_arguments(parser: argparse.ArgumentParser) -> None:
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--seed-bundle", type=Path, help="v1 seed bundle (json.gz), 0050 constituents")
    source.add_argument("--history-export", type=Path, help="history_seed.py export directory (v2)")
    parser.add_argument("--constituents", type=Path, help="constituent weights for --seed-bundle")
    parser.add_argument("--portfolios", default="long9,top10", help="history export portfolios")
    parser.add_argument("--exclude", default="0050,7769", help="tickers excluded from --seed-bundle")


def portfolio_sets(args: argparse.Namespace) -> dict[str, tuple[list[str], list[str], np.ndarray, np.ndarray]]:
    """Label -> (tickers, return dates, asset log returns, weights)."""
    if args.seed_bundle is not None:
        if args.constituents is None:
            raise SystemExit("--constituents is required with --seed-bundle")
        names, dates, returns = load_seed_panel(args.seed_bundle, set(args.exclude.split(",")))
        published = {item["code"]: item["weights"] for item in json.loads(args.constituents.read_text())["constituents"]}
        cap = np.array([published.get(name, 0.0) for name in names])
        return {
            "equal": (names, dates, returns, np.full(len(names), 1 / len(names))),
            "cap": (names, dates, returns, cap / cap.sum()),
        }
    result = {}
    for name in args.portfolios.split(","):
        tickers, dates, returns, weights = load_history_export(args.history_export, name)
        result[f"{name}-snapshot"] = (tickers, dates, returns, weights)
        result[f"{name}-equal"] = (tickers, dates, returns, np.full(len(tickers), 1 / len(tickers)))
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    add_portfolio_arguments(parser)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--lookback", type=int, default=252)
    parser.add_argument("--simulations", type=int, default=10_000)
    parser.add_argument("--repetitions", type=int, default=10_000)
    args = parser.parse_args()

    args.output.mkdir(parents=True, exist_ok=True)
    summary = {
        "model": "VT-GARCH-t + Joint-Vector FHS (production replay)",
        "lookback": args.lookback, "simulations": args.simulations,
        "repetitions": args.repetitions, "refit_interval": REFIT_INTERVAL,
        "levels": list(DEFAULT_LEVELS), "portfolios": {},
    }
    for label, (names, dates, returns, weights) in portfolio_sets(args).items():
        forecasts = collect_forecasts(
            returns, weights, dates, args.lookback, args.simulations, f"es-backtest:{label}",
            keep_previous_on_fit_failure=True,
        )
        result = evaluate(forecasts, DEFAULT_LEVELS, args.repetitions, label)
        summary["portfolios"][label] = {
            "assets": names, "weights": weights.tolist(),
            "hhi": float(np.sum(weights**2)), "observations": int(forecasts.actual.size),
            "from": forecasts.dates[0], "to": forecasts.dates[-1],
            "refit_failures_kept_previous": forecasts.fit_failures, "tests": result,
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
        print(label, "done", flush=True)
    (args.output / "es_backtest_summary.json").write_text(
        json.dumps(summary, indent=1, ensure_ascii=False) + "\n"
    )


if __name__ == "__main__":
    main()
