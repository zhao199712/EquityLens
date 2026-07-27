"""Production VT-GARCH-t and joint-vector FHS implementation."""

from __future__ import annotations

import hashlib
import math
import warnings
from dataclasses import dataclass

import numpy as np
from arch import arch_model
from scipy.optimize import minimize
from scipy.special import gammaln


MODEL_NAME = "VT-GARCH-t + Joint-Vector FHS"
ALGORITHM_VERSION = "vt-garch-t-joint-fhs-v1"
MAX_HEALTHY_PERSISTENCE = 0.995
MIN_STUDENT_NU = 2.05


class GarchFitError(RuntimeError):
    """Raised when a fit cannot produce a healthy predictive distribution."""


@dataclass(frozen=True)
class VtGarchParameters:
    omega: float
    alpha: float
    beta: float
    nu: float
    persistence: float
    optimizer_attempts: int


@dataclass(frozen=True)
class VtGarchFit:
    parameters: tuple[VtGarchParameters, ...]
    residuals: np.ndarray
    next_variances: np.ndarray
    warning_count: int

    @property
    def near_unit_rate(self) -> float:
        return float(np.mean(
            [item.persistence >= MAX_HEALTHY_PERSISTENCE for item in self.parameters]
        ))


@dataclass(frozen=True)
class PathDistribution:
    horizons: dict[int, np.ndarray]
    sample_paths: np.ndarray
    day_quantiles: np.ndarray


def _quantiles_nearest(values: np.ndarray, probs: list[float]) -> np.ndarray:
    ordered = np.sort(values)
    count = ordered.size
    if count == 0:
        return np.zeros(len(probs), dtype=np.float64)
    indices = np.clip(np.ceil(np.asarray(probs) * count).astype(int) - 1, 0, count - 1)
    return ordered[indices]


def deterministic_seed(
    input_hash: str, operation: str, window_offset: int, model_version: str = ALGORITHM_VERSION
) -> int:
    payload = f"{input_hash}:{operation}:{window_offset}:{model_version}".encode()
    return int.from_bytes(hashlib.sha256(payload).digest()[:8], "little") & 0x7FFF_FFFF


def _fit_asset(values: np.ndarray) -> tuple[VtGarchParameters, np.ndarray, float, int]:
    if values.ndim != 1 or values.size < 10 or np.any(~np.isfinite(values)):
        raise GarchFitError("asset history must contain at least ten finite returns")
    target = float(np.var(values, ddof=1))
    if not math.isfinite(target) or target <= 0:
        raise GarchFitError("asset return variance must be positive")

    warning_count = 0
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        preliminary = arch_model(
            values, mean="Zero", vol="GARCH", p=1, o=0, q=1,
            dist="StudentsT", rescale=False,
        ).fit(disp="off", show_warning=False, options={"maxiter": 500})
        warning_count += len(caught)
    preliminary_params = preliminary.params
    alpha0 = float(preliminary_params["alpha[1]"])
    beta0 = float(preliminary_params["beta[1]"])
    nu0 = float(preliminary_params["nu"])
    if alpha0 + beta0 >= 0.98:
        scale = 0.98 / (alpha0 + beta0)
        alpha0, beta0 = alpha0 * scale, beta0 * scale

    def unpack(candidate: np.ndarray) -> tuple[float, float, float, float]:
        alpha, beta, nu = map(float, candidate)
        return target * (1.0 - alpha - beta), alpha, beta, nu

    def variance_path(candidate: np.ndarray) -> np.ndarray:
        omega, alpha, beta, _ = unpack(candidate)
        path = np.empty_like(values)
        path[0] = target
        for index in range(1, values.size):
            path[index] = (
                omega + alpha * values[index - 1] ** 2 + beta * path[index - 1]
            )
        return path

    def objective(candidate: np.ndarray) -> float:
        omega, _, _, nu = unpack(candidate)
        if omega <= 0 or nu <= MIN_STUDENT_NU:
            return 1e100
        variance = variance_path(candidate)
        if np.any(~np.isfinite(variance)) or np.any(variance <= 0):
            return 1e100
        scaled = values**2 / variance
        constant = (
            gammaln((nu + 1) / 2) - gammaln(nu / 2)
            - 0.5 * np.log(np.pi * (nu - 2))
        )
        return float(-np.sum(
            constant - 0.5 * np.log(variance)
            - 0.5 * (nu + 1) * np.log1p(scaled / (nu - 2))
        ))

    selected = None
    attempts = 0
    starts = (
        np.asarray([alpha0, beta0, max(nu0, MIN_STUDENT_NU + 0.01)]),
        np.asarray([0.05, 0.90, 8.0]),
    )
    for start in starts:
        attempts += 1
        candidate = minimize(
            objective,
            start,
            method="SLSQP",
            bounds=((0, .994), (0, .994), (MIN_STUDENT_NU + 1e-6, 500)),
            constraints=({
                "type": "ineq",
                "fun": lambda item: MAX_HEALTHY_PERSISTENCE - 1e-6 - item[0] - item[1],
            },),
            options={"maxiter": 500, "ftol": 1e-9, "disp": False},
        )
        if candidate.success and np.isfinite(candidate.fun):
            selected = candidate
            break
    if selected is None:
        raise GarchFitError("variance-targeted optimization failed")

    omega, alpha, beta, nu = unpack(selected.x)
    persistence = alpha + beta
    variance = variance_path(selected.x)
    next_variance = omega + alpha * values[-1] ** 2 + beta * variance[-1]
    if (
        omega <= 0 or alpha < 0 or beta < 0
        or persistence >= MAX_HEALTHY_PERSISTENCE or nu <= MIN_STUDENT_NU
        or not math.isfinite(next_variance) or next_variance <= 0
    ):
        raise GarchFitError("fitted parameters failed the production health gate")
    residuals = values / np.sqrt(variance)
    if np.any(~np.isfinite(residuals)):
        raise GarchFitError("filtered residuals are non-finite")
    return (
        VtGarchParameters(omega, alpha, beta, nu, persistence, attempts),
        residuals,
        float(next_variance),
        warning_count,
    )


def fit_vt_garch(return_matrix: np.ndarray) -> VtGarchFit:
    """Fit one variance-targeted marginal per row.

    Input and fitted variances use percentage-return units for numerical stability.
    """
    matrix = np.asarray(return_matrix, dtype=np.float64)
    if matrix.ndim != 2 or matrix.shape[1] < 10:
        raise GarchFitError("return matrix must be assets by observations")
    parameters: list[VtGarchParameters] = []
    residual_columns: list[np.ndarray] = []
    forecasts: list[float] = []
    warning_count = 0
    for asset in matrix:
        parameter, residual, forecast, warnings_seen = _fit_asset(asset * 100.0)
        parameters.append(parameter)
        residual_columns.append(residual)
        forecasts.append(forecast)
        warning_count += warnings_seen
    result = VtGarchFit(
        tuple(parameters),
        np.column_stack(residual_columns),
        np.asarray(forecasts, dtype=np.float64),
        warning_count,
    )
    if np.any(~np.isfinite(result.next_variances)) or np.any(result.next_variances <= 0):
        raise GarchFitError("predictive variances are invalid")
    return result


def one_day_distribution(
    fit: VtGarchFit,
    weights: np.ndarray,
    simulations: int,
    seed: int,
) -> np.ndarray:
    weights_array = np.asarray(weights, dtype=np.float64)
    rng = np.random.default_rng(seed)
    sampled = fit.residuals[
        rng.integers(0, fit.residuals.shape[0], size=simulations)
    ]
    simulated = sampled * (np.sqrt(fit.next_variances) / 100.0)
    distribution = 1.0 - weights_array.sum() + np.exp(simulated) @ weights_array - 1.0
    if np.any(~np.isfinite(distribution)):
        raise GarchFitError("predictive distribution contains non-finite values")
    return distribution


def simulate_paths(
    fit: VtGarchFit,
    weights: np.ndarray,
    simulations: int,
    horizon_days: int,
    seed: int,
    retained_paths: int = 20,
) -> PathDistribution:
    """Simulate dynamic GARCH paths while preserving joint residual vectors."""
    if horizon_days <= 0 or simulations <= 0:
        raise ValueError("simulations and horizon must be positive")
    weights_array = np.asarray(weights, dtype=np.float64)
    rng = np.random.default_rng(seed)
    variances = np.broadcast_to(
        fit.next_variances, (simulations, fit.next_variances.size)
    ).copy()
    cumulative = np.ones(simulations, dtype=np.float64)
    sample_count = min(retained_paths, simulations)
    samples = np.empty((sample_count, horizon_days), dtype=np.float64)
    day_quantiles = np.empty((horizon_days, 5), dtype=np.float64)
    horizons: dict[int, np.ndarray] = {}
    requested = {day for day in (1, 7, 30, 252) if day <= horizon_days}

    alpha = np.asarray([item.alpha for item in fit.parameters])
    beta = np.asarray([item.beta for item in fit.parameters])
    omega = np.asarray([item.omega for item in fit.parameters])
    for day in range(1, horizon_days + 1):
        residual = fit.residuals[
            rng.integers(0, fit.residuals.shape[0], size=simulations)
        ]
        percent_return = residual * np.sqrt(variances)
        portfolio_multiplier = (
            1.0 - weights_array.sum()
            + np.exp(percent_return / 100.0) @ weights_array
        )
        cumulative *= portfolio_multiplier
        day_values = cumulative - 1.0
        samples[:, day - 1] = day_values[:sample_count]
        day_quantiles[day - 1] = _quantiles_nearest(
            day_values, [0.01, 0.05, 0.50, 0.95, 0.99]
        )
        if day in requested or day == horizon_days:
            horizons[day] = day_values.copy()
        variances = omega + alpha * percent_return**2 + beta * variances
        if np.any(~np.isfinite(variances)) or np.any(variances <= 0):
            raise GarchFitError("path simulation produced invalid variance")
    return PathDistribution(horizons, samples, day_quantiles)
