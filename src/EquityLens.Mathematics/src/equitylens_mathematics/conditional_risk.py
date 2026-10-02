"""One-day conditional log-return volatility and Euler risk contributions."""

from __future__ import annotations

import math

import numpy as np

from .garch import GarchFitError, VtGarchFit
from .models import RiskAsset


def conditional_risk(
    fit: VtGarchFit, weights: np.ndarray, assets: list[RiskAsset] | None = None
) -> dict:
    weights = np.asarray(weights, dtype=np.float64)
    variances = np.asarray(fit.next_variances, dtype=np.float64)
    residuals = np.asarray(fit.residuals, dtype=np.float64)
    n = weights.size
    if (
        weights.ndim != 1 or n == 0 or variances.shape != (n,)
        or residuals.ndim != 2 or residuals.shape[1] != n or residuals.shape[0] < 2
        or not np.all(np.isfinite(weights)) or np.any(weights < 0)
        or weights.sum() <= 0 or not np.all(np.isfinite(variances))
        or np.any(variances <= 0) or not np.all(np.isfinite(residuals))
    ):
        raise GarchFitError("invalid conditional risk inputs")
    residual_variances = np.var(residuals, axis=0, ddof=1)
    if not np.all(np.isfinite(residual_variances)) or np.any(residual_variances <= 0):
        raise GarchFitError("conditional risk residual variance must be positive")
    if assets is not None and (
        len(assets) != n or len({asset.security_id for asset in assets}) != n
    ):
        raise GarchFitError("conditional risk asset metadata does not match returns")

    correlation = np.eye(1) if n == 1 else np.corrcoef(residuals, rowvar=False)
    # GARCH fits percentage returns; convert standard deviations to decimal units.
    deviations = np.sqrt(variances) / 100.0
    covariance = correlation * np.outer(deviations, deviations)
    if not np.all(np.isfinite(covariance)):
        raise GarchFitError("conditional risk covariance is not finite")
    annualization = math.sqrt(252)

    def volatility(w: np.ndarray) -> float:
        variance = float(w @ covariance @ w)
        # Allow floating-point cancellation for a perfectly hedged portfolio.
        tolerance = 32 * np.finfo(float).eps * float(np.abs(w) @ np.abs(covariance) @ np.abs(w))
        if not math.isfinite(variance) or variance < -tolerance:
            raise GarchFitError("conditional risk portfolio variance is invalid")
        return 0.0 if abs(variance) <= tolerance else math.sqrt(variance)

    daily = volatility(weights)
    annualized = daily * annualization
    marginal = covariance @ weights / daily * annualization if daily > 0 else None

    def contribution(indices: list[int]) -> dict:
        group_weight = float(weights[indices].sum())
        component = float(weights[indices] @ marginal[indices]) if marginal is not None else None
        # A group increment preserves its internal proportions and is funded from cash.
        group_marginal = (
            float(weights[indices] @ marginal[indices]) / group_weight
            if marginal is not None and group_weight > 0 else None
        )
        without = weights.copy()
        without[indices] = 0
        return {
            "weight": group_weight,
            "componentVolatility": component,
            "componentRiskShare": component / annualized if component is not None else None,
            "marginalVolatility": group_marginal,
            "incrementalVolatility": annualized - volatility(without) * annualization,
        }

    holdings = []
    groups: dict[str, list[int]] = {}
    if assets is not None:
        for index, asset in enumerate(assets):
            holding = {
                **asset.model_dump(by_alias=True),
                **contribution([index]),
                "annualizedVolatility": float(deviations[index] * annualization),
                "dataPointCount": residuals.shape[0],
            }
            # A zero-weight asset still has a defined individual marginal derivative.
            holding["marginalVolatility"] = float(marginal[index]) if marginal is not None else None
            holdings.append(holding)
            groups.setdefault(asset.industry, []).append(index)
    industries = [
        {"industry": name, "holdingCount": len(indices), **contribution(indices)}
        for name, indices in groups.items()
    ]
    industries.sort(key=lambda item: item["componentRiskShare"] or 0.0, reverse=True)
    return {
        "methodVersion": "vt-garch-residual-correlation-v1",
        "horizonDays": 1,
        "annualizationDays": 252,
        "dailyVolatility": daily,
        "annualizedVolatility": annualized,
        "holdings": holdings,
        "industries": industries,
    }
