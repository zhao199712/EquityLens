"""Task 6-7 experiments using the immutable Phase-5 adjusted-close payload."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import time
import warnings
from collections import defaultdict
from datetime import date
from pathlib import Path

import numpy as np
from arch import arch_model
from scipy.optimize import minimize
from scipy.special import gammaln

from equitylens_mathematics.engine import (
    ewma_covariances,
    filtered_residuals,
    stable_seed,
    stabilize_covariances,
)
from equitylens_mathematics.research.additional_validation import paired_test, qloss

TICKERS = [
    "2330", "2454", "2308", "2317", "3711", "2303", "2383", "2891",
    "2344", "2345", "2881", "2882", "1303", "2382", "2887", "2360",
    "3017", "2885", "2412", "2886",
]
RAW_WEIGHTS = np.asarray(
    [57.37, 6.11, 3.67, 2.99, 2.20, 1.87, 1.53, 1.23, 0.62, 1.21,
     1.05, 0.91, 0.80, 0.95, 0.75, 0.77, 0.79, 0.71, 0.53, 0.53]
)
MARKET = RAW_WEIGHTS / RAW_WEIGHTS.sum()
EQUAL = np.full(20, 0.05)
SIMULATIONS = 5_000
CONFIDENCES = (0.95, 0.99)


def read_returns(path: Path) -> tuple[list[date], np.ndarray]:
    rows = json.loads(path.read_text())
    by_date: dict[str, dict[str, float]] = defaultdict(dict)
    for row in rows:
        by_date[row["date"]][row["ticker"]] = float(row["close"])
    keys = sorted(by_date)
    prices = np.full((len(keys), len(TICKERS)), np.nan)
    for i, key in enumerate(keys):
        for j, ticker in enumerate(TICKERS):
            if ticker in by_date[key]:
                prices[i, j] = by_date[key][ticker]
    valid = np.all(np.isfinite(prices) & (prices > 0), axis=1)
    first = int(np.flatnonzero(valid)[0])
    keys, prices, valid = keys[first:], prices[first:], valid[first:]
    good = valid[1:] & valid[:-1]
    values = np.log(prices[1:] / prices[:-1])[good].T
    dates = [date.fromisoformat(keys[i + 1]) for i, ok in enumerate(good) if ok]
    return dates, values


def fit_vt(values: np.ndarray) -> tuple[dict[str, float], np.ndarray, float, int]:
    target = float(np.var(values, ddof=1))
    preliminary = arch_model(
        values, mean="Zero", vol="GARCH", p=1, o=0, q=1,
        dist="StudentsT", rescale=False
    ).fit(disp="off", show_warning=False, options={"maxiter": 500})
    p = preliminary.params
    a0, b0, n0 = float(p["alpha[1]"]), float(p["beta[1]"]), float(p["nu"])
    if a0 + b0 >= 0.98:
        scale = 0.98 / (a0 + b0)
        a0, b0 = a0 * scale, b0 * scale

    def unpack(x: np.ndarray) -> tuple[float, float, float, float]:
        alpha, beta, nu = map(float, x)
        return target * (1 - alpha - beta), alpha, beta, nu

    def path(x: np.ndarray) -> np.ndarray:
        omega, alpha, beta, _ = unpack(x)
        result = np.empty_like(values)
        result[0] = target
        for i in range(1, len(values)):
            result[i] = omega + alpha * values[i - 1] ** 2 + beta * result[i - 1]
        return result

    def objective(x: np.ndarray) -> float:
        omega, _, _, nu = unpack(x)
        if omega <= 0 or nu <= 2:
            return 1e100
        variance = path(x)
        if np.any(~np.isfinite(variance)) or np.any(variance <= 0):
            return 1e100
        z2 = values**2 / variance
        constant = gammaln((nu + 1) / 2) - gammaln(nu / 2) - 0.5 * np.log(np.pi * (nu - 2))
        return float(-np.sum(constant - 0.5 * np.log(variance) - 0.5 * (nu + 1) * np.log1p(z2 / (nu - 2))))

    selected = None
    attempts = 0
    for start in (np.asarray([a0, b0, max(n0, 2.1)]), np.asarray([0.05, 0.90, 8.0])):
        attempts += 1
        candidate = minimize(
            objective, start, method="SLSQP",
            bounds=((0, .999), (0, .999), (2.05, 500)),
            constraints=({"type": "ineq", "fun": lambda x: .999999 - x[0] - x[1]},),
            options={"maxiter": 500, "ftol": 1e-9, "disp": False},
        )
        if candidate.success and np.isfinite(candidate.fun):
            selected = candidate
            break
    if selected is None:
        raise RuntimeError("variance-targeted optimization failed")
    omega, alpha, beta, nu = unpack(selected.x)
    variance = path(selected.x)
    next_variance = omega + alpha * values[-1] ** 2 + beta * variance[-1]
    return (
        {"omega": omega, "alpha": alpha, "beta": beta, "nu": nu,
         "persistence": alpha + beta},
        values / np.sqrt(variance),
        next_variance,
        attempts,
    )


def var_es(values: np.ndarray, confidence: float) -> tuple[float, float]:
    ordered = np.sort(values)
    rank = max(0, math.ceil((1 - confidence) * len(ordered)) - 1)
    var = float(ordered[rank])
    return var, float(ordered[ordered <= var].mean())


def ewma_distribution(matrix: np.ndarray, weights: np.ndarray, seed: int) -> np.ndarray:
    covariance = stabilize_covariances(ewma_covariances(matrix, .94), .10)
    lower = np.linalg.cholesky(covariance)
    residual = filtered_residuals(matrix, lower)
    rng = np.random.default_rng(seed)
    selected = residual[rng.integers(0, len(residual), SIMULATIONS)]
    simulated = selected @ lower[-1].T
    return np.exp(simulated) @ weights - 1


def cap_weights(weights: np.ndarray, cap: float) -> np.ndarray:
    result = weights.copy()
    while np.any(result > cap + 1e-15):
        over = result > cap
        excess = float((result[over] - cap).sum())
        result[over] = cap
        free = ~over
        result[free] += excess * result[free] / result[free].sum()
    return result / result.sum()


def fixtures(returns: np.ndarray) -> tuple[list[str], np.ndarray]:
    inverse = 1 / np.std(returns, axis=1, ddof=1)
    inverse /= inverse.sum()
    rng = np.random.default_rng(20260726)
    random = rng.dirichlet(np.ones(20), size=200)
    names = ["market", "equal", "cap20", "cap10", "inverse_vol"] + [
        f"dirichlet_{i:03d}" for i in range(1, 201)
    ]
    return names, np.vstack((MARKET, EQUAL, cap_weights(MARKET, .20), cap_weights(MARKET, .10), inverse, random))


def rolling_win(loss_g: np.ndarray, loss_m: np.ndarray) -> float:
    wins = []
    for start in range(0, len(loss_g) - 252 + 1, 63):
        wins.append(loss_g[start:start + 252].mean() < loss_m[start:start + 252].mean())
    return float(np.mean(wins))


def run_sensitivity(dates: list[date], returns: np.ndarray) -> list[dict]:
    rows = []
    for lookback in (252, 504, 756):
        for refit in (5, 21, 63):
            started = time.perf_counter()
            weights_matrix = np.vstack((MARKET, EQUAL))
            hashes = [hashlib.sha256(returns.tobytes() + w.tobytes()).hexdigest() for w in weights_matrix]
            actual = np.exp(returns.T) @ weights_matrix.T - 1
            mvar = {c: [[], []] for c in CONFIDENCES}
            gvar = {c: [[], []] for c in CONFIDENCES}
            ges = {c: [[], []] for c in CONFIDENCES}
            params: list[list[dict]] | None = None
            residual: np.ndarray | None = None
            next_var: np.ndarray | None = None
            history = []
            failures = warning_count = attempts = 0
            fit_count = 0
            for offset, index in enumerate(range(lookback, returns.shape[1])):
                if offset % refit == 0:
                    columns, forecasts, params = [], [], []
                    for asset in range(20):
                        window = returns[asset, index - lookback:index] * 100
                        with warnings.catch_warnings(record=True) as caught:
                            warnings.simplefilter("always")
                            try:
                                p, z, forecast, used = fit_vt(window)
                            except Exception:
                                failures += 1
                                raise
                        warning_count += len(caught)
                        attempts += used
                        fit_count += 1
                        params.append(p)
                        history.append({**p, "offset": offset, "asset": asset})
                        columns.append(z)
                        forecasts.append(forecast)
                    residual = np.column_stack(columns)
                    next_var = np.asarray(forecasts)
                assert params is not None and residual is not None and next_var is not None
                for track, weights in enumerate(weights_matrix):
                    md = ewma_distribution(
                        returns[:, index - lookback:index], weights,
                        stable_seed(hashes[track], offset, False)
                    )
                    rng = np.random.default_rng(stable_seed(f"{hashes[track]}:garch", offset, False))
                    sampled = residual[rng.integers(0, len(residual), SIMULATIONS)]
                    gd = np.exp(sampled * (np.sqrt(next_var) / 100)) @ weights - 1
                    for confidence in CONFIDENCES:
                        mvar[confidence][track].append(var_es(md, confidence)[0])
                        gv, ge = var_es(gd, confidence)
                        gvar[confidence][track].append(gv)
                        ges[confidence][track].append(ge)
                current = returns[:, index] * 100
                residual = np.vstack((residual[1:], current / np.sqrt(next_var)))
                next_var = np.asarray([
                    p["omega"] + p["alpha"] * current[a] ** 2 + p["beta"] * next_var[a]
                    for a, p in enumerate(params)
                ])
            h = history
            persistence = np.asarray([p["persistence"] for p in h])
            parameter_jumps = []
            for asset in range(20):
                sequence = [p for p in h if p["asset"] == asset]
                for left, right in zip(sequence[:-1], sequence[1:]):
                    parameter_jumps.append(
                        max(
                            abs(right[key] - left[key])
                            for key in ("alpha", "beta", "nu", "persistence")
                        )
                    )
            for track, name in enumerate(("market", "equal")):
                observed = actual[lookback:, track]
                for confidence in CONFIDENCES:
                    mloss = qloss(observed, np.asarray(mvar[confidence][track]), confidence)
                    gloss = qloss(observed, np.asarray(gvar[confidence][track]), confidence)
                    dm = paired_test(gloss - mloss, f"sensitivity:{lookback}:{refit}:{name}:{confidence}")
                    breach = observed < np.asarray(gvar[confidence][track])
                    ratio = abs(observed[breach].mean()) / abs(np.asarray(ges[confidence][track])[breach].mean())
                    rows.append({
                        "lookback": lookback, "refit": refit, "weighting": name,
                        "confidence": confidence, "runtime_seconds": time.perf_counter() - started,
                        "fit_count": fit_count, "convergence_failures": failures,
                        "warnings": warning_count, "optimizer_attempts": attempts,
                        "near_unit_count": int((persistence >= .995).sum()),
                        "near_unit_rate": float((persistence >= .995).mean()),
                        "alpha_median": float(np.median([p["alpha"] for p in h])),
                        "alpha_q1": float(np.quantile([p["alpha"] for p in h], .25)),
                        "alpha_q3": float(np.quantile([p["alpha"] for p in h], .75)),
                        "beta_median": float(np.median([p["beta"] for p in h])),
                        "beta_q1": float(np.quantile([p["beta"] for p in h], .25)),
                        "beta_q3": float(np.quantile([p["beta"] for p in h], .75)),
                        "nu_median": float(np.median([p["nu"] for p in h])),
                        "nu_q1": float(np.quantile([p["nu"] for p in h], .25)),
                        "nu_q3": float(np.quantile([p["nu"] for p in h], .75)),
                        "persistence_median": float(np.median(persistence)),
                        "persistence_q1": float(np.quantile(persistence, .25)),
                        "persistence_q3": float(np.quantile(persistence, .75)),
                        "adjacent_refit_parameter_jump_median": float(np.median(parameter_jumps)),
                        "adjacent_refit_parameter_jump_max": float(np.max(parameter_jumps)),
                        "garch_var_path_jump_median": float(np.median(np.abs(np.diff(gvar[confidence][track])))),
                        "garch_var_path_jump_max": float(np.max(np.abs(np.diff(gvar[confidence][track])))),
                        "mean_ql_mvewma": float(mloss.mean()), "mean_ql_garch": float(gloss.mean()),
                        "dm_p": dm["twoSidedPValue"], "dm_delta": dm["meanDifference"],
                        "rolling_garch_win_rate": rolling_win(gloss, mloss) if len(gloss) >= 252 else float("nan"),
                        "garch_breach_count": int(breach.sum()), "garch_breach_rate": float(breach.mean()),
                        "garch_es_ratio": float(ratio),
                    })
    return rows


def run_concentration(dates: list[date], returns: np.ndarray) -> tuple[list[dict], list[dict]]:
    names, weights_matrix = fixtures(returns)
    manifest = []
    for name, weights in zip(names, weights_matrix):
        for ticker, weight in zip(TICKERS, weights):
            manifest.append({"fixture": name, "ticker": ticker, "weight": weight})
    lookback, refit = 252, 21
    hashes = [hashlib.sha256(returns.tobytes() + w.tobytes()).hexdigest() for w in weights_matrix]
    actual = np.exp(returns.T) @ weights_matrix.T - 1
    sums = {(i, c): [0.0, 0.0, 0, 0, 0.0, 0.0] for i in range(len(names)) for c in CONFIDENCES}
    params = residual = next_var = None
    for offset, index in enumerate(range(lookback, returns.shape[1])):
        if offset % refit == 0:
            columns, forecasts, params = [], [], []
            for asset in range(20):
                p, z, forecast, _ = fit_vt(returns[asset, index - lookback:index] * 100)
                params.append(p); columns.append(z); forecasts.append(forecast)
            residual, next_var = np.column_stack(columns), np.asarray(forecasts)
        assert params is not None and residual is not None and next_var is not None
        matrix = returns[:, index - lookback:index]
        covariance = stabilize_covariances(ewma_covariances(matrix, .94), .10)
        lower = np.linalg.cholesky(covariance)
        ewma_residual = filtered_residuals(matrix, lower)
        for fixture, weights in enumerate(weights_matrix):
            rng_m = np.random.default_rng(stable_seed(hashes[fixture], offset, False))
            sampled_m = ewma_residual[rng_m.integers(0, len(ewma_residual), SIMULATIONS)]
            md = np.exp(sampled_m @ lower[-1].T) @ weights - 1
            rng_g = np.random.default_rng(stable_seed(f"{hashes[fixture]}:garch", offset, False))
            sampled_g = residual[rng_g.integers(0, len(residual), SIMULATIONS)]
            gd = np.exp(sampled_g * (np.sqrt(next_var) / 100)) @ weights - 1
            observed = actual[index, fixture]
            for confidence in CONFIDENCES:
                mv, me = var_es(md, confidence); gv, ge = var_es(gd, confidence)
                lm = float(qloss(np.asarray([observed]), np.asarray([mv]), confidence)[0])
                lg = float(qloss(np.asarray([observed]), np.asarray([gv]), confidence)[0])
                state = sums[(fixture, confidence)]
                state[0] += lg - lm; state[1] += float(observed < gv) - float(observed < mv)
                state[2] += int(observed < gv); state[3] += int(observed < mv)
                if observed < gv: state[4] += abs(observed) / abs(ge)
                if observed < mv: state[5] += abs(observed) / abs(me)
        current = returns[:, index] * 100
        residual = np.vstack((residual[1:], current / np.sqrt(next_var)))
        next_var = np.asarray([p["omega"] + p["alpha"] * current[a] ** 2 + p["beta"] * next_var[a] for a, p in enumerate(params)])
    rows = []
    n = returns.shape[1] - lookback
    for fixture, (name, weights) in enumerate(zip(names, weights_matrix)):
        hhi = float(np.sum(weights**2))
        for confidence in CONFIDENCES:
            state = sums[(fixture, confidence)]
            rows.append({
                "fixture": name, "confidence": confidence, "hhi": hhi,
                "effective_assets": 1 / hhi, "delta_ql_garch_minus_mvewma": state[0] / n,
                "breach_rate_difference": state[1] / n,
                "es_ratio_difference": state[4] / max(state[2], 1) - state[5] / max(state[3], 1),
            })
    return manifest, rows


def write_csv(path: Path, rows: list[dict]) -> None:
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader(); writer.writerows(rows)


def concentration_regressions(rows: list[dict]) -> dict:
    output = {}
    for confidence in CONFIDENCES:
        subset = [row for row in rows if row["confidence"] == confidence]
        y = np.asarray([row["delta_ql_garch_minus_mvewma"] for row in subset])
        output[str(confidence)] = {}
        for field in ("hhi", "effective_assets"):
            x = np.column_stack((np.ones(len(subset)), [row[field] for row in subset]))
            beta = np.linalg.solve(x.T @ x, x.T @ y)
            residual = y - x @ beta
            bread = np.linalg.inv(x.T @ x)
            meat = x.T @ np.diag(residual**2) @ x
            covariance = len(y) / (len(y) - x.shape[1]) * bread @ meat @ bread
            se = np.sqrt(np.diag(covariance))
            output[str(confidence)][field] = {
                "observations": len(y),
                "intercept": float(beta[0]),
                "slope": float(beta[1]),
                "hc1RobustStandardError": float(se[1]),
                "slopeCi95": [float(beta[1] - 1.96 * se[1]), float(beta[1] + 1.96 * se[1])],
            }
    return output


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--task", choices=("sensitivity", "concentration", "all"), default="all")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    dates, returns = read_returns(args.input)
    if args.task in ("sensitivity", "all"):
        write_csv(args.output / "refit_lookback_sensitivity.csv", run_sensitivity(dates, returns))
    if args.task in ("concentration", "all"):
        manifest, concentration = run_concentration(dates, returns)
        write_csv(args.output / "portfolio_weight_manifest.csv", manifest)
        write_csv(args.output / "concentration_robustness.csv", concentration)
        (args.output / "concentration_regression.json").write_text(
            json.dumps(concentration_regressions(concentration), indent=2) + "\n"
        )


if __name__ == "__main__":
    main()
