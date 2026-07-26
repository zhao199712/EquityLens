from __future__ import annotations

import hashlib
import math
import time
from dataclasses import dataclass

import numpy as np

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
        regular = simulate_fhs(
            matrix,
            weights,
            input_data.simulations,
            input_data.confidence_levels,
            input_data.ewma_lambda,
            input_data.shrinkage_alpha,
            0.0,
            stable_seed(input_hash, offset, False),
        )
        conservative = simulate_fhs(
            matrix,
            weights,
            input_data.simulations,
            input_data.confidence_levels,
            input_data.ewma_lambda,
            input_data.shrinkage_alpha,
            input_data.conservative_residual_cap_quantile,
            stable_seed(input_hash, offset, True),
        )
        windows.append(
            {
                "date": input_data.return_dates[index].isoformat(),
                "actual": float(portfolio_returns[index]),
                "historical": historical,
                "regular": regular,
                "conservative": conservative,
            }
        )

    models: list[dict] = []
    for confidence_index, confidence in enumerate(input_data.confidence_levels):
        for name, key in (
            ("Historical", "historical"),
            ("MVEWMA-FHS", "regular"),
            ("MVEWMA-FHS（保守 p99）", "conservative"),
        ):
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
        },
        "errorCode": None,
        "errorMessage": None,
        "_durationMs": round((time.perf_counter() - started) * 1000),
    }
