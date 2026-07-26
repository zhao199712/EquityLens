from __future__ import annotations

import hashlib
import math
import time
from dataclasses import dataclass

import numpy as np

from .garch import (
    ALGORITHM_VERSION,
    MODEL_NAME,
    VtGarchFit,
    deterministic_seed,
    fit_vt_garch,
    one_day_distribution,
    simulate_paths,
)
from .models import RiskBacktestInput


@dataclass(frozen=True)
class SimulationResult:
    var: list[float]
    es: list[float]


def nearest_rank(values: np.ndarray, probability: float) -> float:
    if values.size == 0:
        return 0.0
    ordered = np.sort(values)
    index = max(0, min(math.ceil(probability * ordered.size) - 1, ordered.size - 1))
    return float(ordered[index])


def historical_var(values: np.ndarray, confidence: float) -> float:
    return nearest_rank(values, 1.0 - confidence)


def expected_shortfall(values: np.ndarray, confidence: float) -> float:
    threshold = historical_var(values, confidence)
    tail = values[values <= threshold]
    return float(tail.mean()) if tail.size else threshold


def ewma_covariances(return_matrix: np.ndarray, decay: float) -> np.ndarray:
    initial = np.cov(return_matrix, ddof=1)
    if return_matrix.shape[0] == 1:
        initial = np.array([[float(initial)]], dtype=np.float64)
    current = np.asarray(initial, dtype=np.float64)
    observations = return_matrix.T
    shocks = np.einsum("ti,tj->tij", observations, observations)
    result = np.empty_like(shocks)
    for index in range(observations.shape[0]):
        current = decay * current + (1.0 - decay) * shocks[index]
        result[index] = current
    return result


def stabilize_covariances(
    covariances: np.ndarray, shrinkage_alpha: float
) -> np.ndarray:
    asset_count = covariances.shape[1]
    identity = np.eye(asset_count, dtype=np.float64)
    diagonal = covariances * identity
    shrunk = diagonal + (1.0 - shrinkage_alpha) * (covariances - diagonal)
    epsilon = np.maximum(
        1e-8 * np.trace(shrunk, axis1=1, axis2=2) / max(asset_count, 1),
        1e-10,
    )
    return shrunk + epsilon[:, None, None] * identity


def filtered_residuals(
    return_matrix: np.ndarray, lower_triangular: np.ndarray
) -> np.ndarray:
    observations = return_matrix.T[..., None]
    return np.linalg.solve(lower_triangular, observations).squeeze(-1)


def simulate_fhs(
    return_matrix: np.ndarray,
    weights: np.ndarray,
    simulations: int,
    confidence_levels: list[float],
    decay: float,
    shrinkage_alpha: float,
    residual_cap_quantile: float,
    seed: int,
) -> SimulationResult:
    covariances = stabilize_covariances(
        ewma_covariances(return_matrix, decay), shrinkage_alpha
    )
    lower_triangular = np.linalg.cholesky(covariances)
    latest_lower = lower_triangular[-1]
    residuals = filtered_residuals(return_matrix, lower_triangular)
    if residuals.shape[0] < 10:
        raise ValueError("fewer than ten filtered residuals")

    if residual_cap_quantile > 0:
        norms = np.linalg.norm(residuals, axis=1)
        cap = nearest_rank(norms, residual_cap_quantile)
        over = norms > cap
        residuals = residuals.copy()
        residuals[over] *= (cap / norms[over])[:, None]

    rng = np.random.default_rng(seed)
    sampled = residuals[rng.integers(0, residuals.shape[0], size=simulations)]
    simulated_log_returns = sampled @ latest_lower.T
    multipliers = 1.0 - weights.sum() + np.exp(simulated_log_returns) @ weights
    distribution = multipliers - 1.0
    return SimulationResult(
        [historical_var(distribution, level) for level in confidence_levels],
        [expected_shortfall(distribution, level) for level in confidence_levels],
    )


def stable_seed(input_hash: str, window_offset: int, conservative: bool) -> int:
    value = f"{input_hash}:{window_offset}:{int(conservative)}".encode()
    return int.from_bytes(hashlib.sha256(value).digest()[:8], "little") & 0x7FFF_FFFF


def _erfc(value: float) -> float:
    return math.erfc(value)


def _kupiec(count: int, breaches: int, expected: float) -> float:
    observed = breaches / count

    def likelihood(probability: float, hits: int) -> float:
        probability = min(max(probability, 0.000001), 0.999999)
        return hits * math.log(probability) + (count - hits) * math.log(1 - probability)

    statistic = -2.0 * (likelihood(expected, breaches) - likelihood(observed, breaches))
    return _erfc(math.sqrt(max(statistic, 0.0) / 2.0))


def _christoffersen(points: list[dict]) -> float | None:
    transitions = [[0, 0], [0, 0]]
    for previous, current in zip(points, points[1:]):
        transitions[int(previous["breached"])][int(current["breached"])] += 1
    n0 = sum(transitions[0])
    n1 = sum(transitions[1])
    if n0 == 0 or n1 == 0:
        return None
    probability = (transitions[0][1] + transitions[1][1]) / (n0 + n1)
    probability0 = transitions[0][1] / n0
    probability1 = transitions[1][1] / n1

    def likelihood(value: float, misses: int, hits: int) -> float:
        value = min(max(value, 0.000001), 0.999999)
        return misses * math.log(1 - value) + hits * math.log(value)

    statistic = -2.0 * (
        likelihood(
            probability,
            transitions[0][0] + transitions[1][0],
            transitions[0][1] + transitions[1][1],
        )
        - likelihood(probability0, transitions[0][0], transitions[0][1])
        - likelihood(probability1, transitions[1][0], transitions[1][1])
    )
    return _erfc(math.sqrt(max(statistic, 0.0) / 2.0))


def build_model(model: str, confidence: float, points: list[dict]) -> dict:
    breaches = sum(point["breached"] for point in points)
    expected = 1.0 - confidence
    tail = [point for point in points if point["breached"]]
    actual_average = (
        sum(point["actualReturn"] for point in tail) / len(tail) if tail else None
    )
    es_average = (
        sum(point["predictedES"] for point in tail) / len(tail) if tail else None
    )
    ratio = (
        actual_average / es_average
        if actual_average is not None and es_average not in (None, 0)
        else None
    )
    es_status = (
        "insufficient_tail_observations"
        if ratio is None
        else "underestimated"
        if ratio > 1.10
        else "conservative"
        if ratio < 0.90
        else "aligned"
    )
    return {
        "model": model,
        "confidenceLevel": confidence,
        "observationCount": len(points),
        "breachCount": breaches,
        "breachRate": breaches / len(points) if points else 0,
        "expectedBreachRate": expected,
        "kupiecPValue": _kupiec(len(points), breaches, expected)
        if len(points) >= 100
        else None,
        "christoffersenPValue": _christoffersen(points)
        if len(points) >= 100
        else None,
        "tailObservationCount": len(tail),
        "actualTailLossAverage": actual_average,
        "predictedEsAverage": es_average,
        "esTailLossRatio": ratio,
        "esStatus": es_status,
        "status": "ready" if len(points) >= 100 else "insufficient_observations",
        "points": points,
    }


def calculate_backtest(input_data: RiskBacktestInput, input_hash: str) -> dict:
    started = time.perf_counter()
    asset_returns = np.asarray(input_data.asset_returns, dtype=np.float64)
    portfolio_returns = np.asarray(input_data.portfolio_returns, dtype=np.float64)
    weights = np.asarray(input_data.weights, dtype=np.float64)
    windows: list[dict] = []
    active_fit: VtGarchFit | None = None

    for offset, index in enumerate(
        range(input_data.lookback_days, portfolio_returns.size)
    ):
        matrix = asset_returns[:, index - input_data.lookback_days : index]
        portfolio_window = portfolio_returns[index - input_data.lookback_days : index]
        historical = SimulationResult(
            [
                historical_var(portfolio_window, confidence)
                for confidence in input_data.confidence_levels
            ],
            [
                expected_shortfall(portfolio_window, confidence)
                for confidence in input_data.confidence_levels
            ],
        )
        if active_fit is None or offset % 21 == 0:
            active_fit = fit_vt_garch(matrix)
        distribution = one_day_distribution(
            active_fit,
            weights,
            input_data.simulations,
            deterministic_seed(input_hash, "backtest", offset),
        )
        regular = SimulationResult(
            [historical_var(distribution, level) for level in input_data.confidence_levels],
            [expected_shortfall(distribution, level) for level in input_data.confidence_levels],
        )
        windows.append(
            {
                "date": input_data.return_dates[index].isoformat(),
                "actual": float(portfolio_returns[index]),
                "historical": historical,
                "regular": regular,
            }
        )
        current = asset_returns[:, index] * 100.0
        parameters = active_fit.parameters
        standardized = current / np.sqrt(active_fit.next_variances)
        residuals = np.vstack((active_fit.residuals[1:], standardized))
        next_variances = np.asarray([
            item.omega + item.alpha * current[asset] ** 2
            + item.beta * active_fit.next_variances[asset]
            for asset, item in enumerate(parameters)
        ])
        active_fit = VtGarchFit(
            parameters, residuals, next_variances, active_fit.warning_count
        )

    models: list[dict] = []
    for confidence_index, confidence in enumerate(input_data.confidence_levels):
        for name, key in (("Historical", "historical"), (MODEL_NAME, "regular")):
            points = []
            for window in windows:
                simulation = window[key]
                predicted_var = simulation.var[confidence_index]
                predicted_es = simulation.es[confidence_index]
                points.append(
                    {
                        "date": window["date"],
                        "actualReturn": window["actual"],
                        "predictedVaR": predicted_var,
                        "predictedES": predicted_es,
                        "breached": window["actual"] < predicted_var,
                    }
                )
            models.append(build_model(name, confidence, points))

    return {
        "outcome": "Success",
        "value": {
            "portfolioId": input_data.portfolio_id,
            "from": input_data.from_date.isoformat(),
            "to": input_data.to_date.isoformat(),
            "lookbackDays": input_data.lookback_days,
            "observationCount": len(windows),
            "models": models,
            "requestedModel": MODEL_NAME,
            "selectedModel": MODEL_NAME,
            "algorithmVersion": ALGORITHM_VERSION,
            "fallbackDepth": 0,
        },
        "errorCode": None,
        "errorMessage": None,
        "_durationMs": round((time.perf_counter() - started) * 1000),
    }


def calculate_current_risk(
    input_data: RiskBacktestInput, input_hash: str, operation: str
) -> dict:
    """Calculate the current multi-horizon VT-GARCH distribution."""
    started = time.perf_counter()
    asset_returns = np.asarray(input_data.asset_returns, dtype=np.float64)
    weights = np.asarray(input_data.weights, dtype=np.float64)
    portfolio_returns = np.asarray(input_data.portfolio_returns, dtype=np.float64)
    matrix = asset_returns[:, -input_data.lookback_days:]
    fit = fit_vt_garch(matrix)
    if portfolio_returns.size >= 2:
        historical_annualized_volatility = float(
            np.std(portfolio_returns, ddof=1) * math.sqrt(252)
        )
        wealth = np.concatenate(([1.0], np.exp(np.cumsum(portfolio_returns))))
        peak = np.maximum.accumulate(wealth)
        max_drawdown = float(np.min((wealth - peak) / peak))
    else:
        historical_annualized_volatility = 0.0
        max_drawdown = 0.0
    horizon_days = 252 if operation == "monte-carlo" else 30
    simulations = max(input_data.simulations, 10_000)
    paths = simulate_paths(
        fit,
        weights,
        simulations,
        horizon_days,
        deterministic_seed(input_hash, operation, 0),
    )
    horizons = []
    for day, distribution in sorted(paths.horizons.items()):
        levels = []
        for confidence in input_data.confidence_levels:
            levels.append({
                "confidenceLevel": confidence,
                "var": historical_var(distribution, confidence),
                "expectedShortfall": expected_shortfall(distribution, confidence),
            })
        horizons.append({
            "horizonDays": day,
            "confidenceLevels": levels,
            "p1": nearest_rank(distribution, .01),
            "p5": nearest_rank(distribution, .05),
            "p50": nearest_rank(distribution, .50),
            "p95": nearest_rank(distribution, .95),
            "p99": nearest_rank(distribution, .99),
            "expectedReturn": float(np.mean(distribution)),
        })
    return {
        "outcome": "Success",
        "value": {
            "portfolioId": input_data.portfolio_id,
            "dataAsOfDate": input_data.return_dates[-1].isoformat(),
            "operation": operation,
            "requestedModel": MODEL_NAME,
            "selectedModel": MODEL_NAME,
            "algorithmVersion": ALGORITHM_VERSION,
            "fallbackDepth": 0,
            "simulations": simulations,
            "lookbackDays": input_data.lookback_days,
            "historicalAnnualizedVolatility": historical_annualized_volatility,
            "maxDrawdown": max_drawdown,
            "horizons": horizons,
            "samplePaths": paths.sample_paths.tolist() if operation == "monte-carlo" else [],
            "fitHealth": {
                "healthy": True,
                "warningCount": fit.warning_count,
                "nearUnitRate": fit.near_unit_rate,
                "maxPersistence": max(item.persistence for item in fit.parameters),
                "minNu": min(item.nu for item in fit.parameters),
                "optimizerAttempts": sum(item.optimizer_attempts for item in fit.parameters),
            },
        },
        "errorCode": None,
        "errorMessage": None,
        "_durationMs": round((time.perf_counter() - started) * 1000),
    }
