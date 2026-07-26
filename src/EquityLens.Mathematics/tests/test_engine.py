from datetime import date, timedelta
import hashlib
from pathlib import Path

import numpy as np

from equitylens_mathematics.engine import (
    calculate_backtest,
    calculate_current_risk,
    ewma_covariances,
    expected_shortfall,
    historical_var,
    stabilize_covariances,
)
from equitylens_mathematics.models import RiskBacktestInput
from equitylens_mathematics.garch import (
    ALGORITHM_VERSION,
    MODEL_NAME,
    deterministic_seed,
    fit_vt_garch,
    one_day_distribution,
    simulate_paths,
    GarchFitError,
)
import pytest


def fixture_input() -> RiskBacktestInput:
    observations = 112
    dates = [date(2025, 1, 1) + timedelta(days=index) for index in range(observations)]
    first = [0.001 * ((index % 7) - 3) for index in range(observations)]
    second = [0.0008 * (((index + 2) % 9) - 4) for index in range(observations)]
    portfolio = [0.6 * x + 0.4 * y for x, y in zip(first, second)]
    return RiskBacktestInput.model_validate(
        {
            "portfolioId": "00000000-0000-0000-0000-000000000001",
            "from": "2025-01-01",
            "to": "2026-01-01",
            "lookbackDays": 100,
            "simulations": 1000,
            "confidenceLevels": [0.95, 0.99],
            "ewmaLambda": 0.94,
            "shrinkageAlpha": 0.05,
            "conservativeResidualCapQuantile": 0.99,
            "returnDates": [value.isoformat() for value in dates],
            "portfolioReturns": portfolio,
            "assetReturns": [first, second],
            "weights": [0.6, 0.4],
        }
    )


def test_historical_tail_metrics_use_nearest_rank() -> None:
    values = np.asarray([-0.10, -0.04, -0.02, 0.01, 0.03])
    assert historical_var(values, 0.80) == -0.10
    assert expected_shortfall(values, 0.80) == -0.10


def test_stabilized_covariances_are_positive_definite() -> None:
    matrix = np.asarray([[0.01, 0.02, -0.01], [0.01, 0.02, -0.01]])
    covariances = stabilize_covariances(ewma_covariances(matrix, 0.94), 0.10)
    for covariance in covariances:
        np.linalg.cholesky(covariance)


def test_backtest_is_reproducible_and_has_expected_models() -> None:
    input_data = fixture_input()
    first = calculate_backtest(input_data, "a" * 64)
    second = calculate_backtest(input_data, "a" * 64)
    assert first["value"] == second["value"]
    assert len(first["value"]["models"]) == 4
    assert first["value"]["observationCount"] == 12
    assert first["value"]["selectedModel"] == MODEL_NAME
    assert first["value"]["algorithmVersion"] == ALGORITHM_VERSION


def test_vt_garch_joint_vector_paths_are_deterministic_and_finite() -> None:
    rng = np.random.default_rng(20260727)
    common = rng.standard_t(8, size=300) * 0.006
    matrix = np.vstack((
        common + rng.normal(0, 0.003, size=300),
        0.5 * common + rng.normal(0, 0.004, size=300),
    ))
    fit = fit_vt_garch(matrix)
    seed = deterministic_seed("a" * 64, "monte-carlo", 0)
    first = simulate_paths(fit, np.asarray([0.6, 0.4]), 200, 30, seed)
    second = simulate_paths(fit, np.asarray([0.6, 0.4]), 200, 30, seed)
    assert np.array_equal(first.sample_paths, second.sample_paths)
    assert np.array_equal(first.horizons[30], second.horizons[30])
    assert np.all(np.isfinite(first.sample_paths))
    one_day = one_day_distribution(fit, np.asarray([0.6, 0.4]), 200, seed)
    assert one_day.shape == (200,)


def test_vt_garch_rejects_zero_variance_without_silent_repair() -> None:
    with pytest.raises(GarchFitError, match="variance must be positive"):
        fit_vt_garch(np.zeros((2, 252)))


def test_current_risk_uses_one_distribution_for_all_confidence_levels() -> None:
    input_data = fixture_input()
    result = calculate_current_risk(input_data, "b" * 64, "risk")
    assert result["outcome"] == "Success"
    assert result["value"]["selectedModel"] == MODEL_NAME
    assert result["value"]["fitHealth"]["healthy"] is True
    assert {item["horizonDays"] for item in result["value"]["horizons"]} == {1, 7, 30}
    assert all(len(item["confidenceLevels"]) == 2 for item in result["value"]["horizons"])


def test_shared_cross_language_fixture_is_accepted() -> None:
    fixture_path = Path(__file__).parents[3] / "tests" / "fixtures" / "risk_backtest_input.json"
    raw = fixture_path.read_text(encoding="utf-8")
    input_data = RiskBacktestInput.model_validate_json(raw)

    result = calculate_backtest(input_data, hashlib.sha256(raw.encode()).hexdigest())

    assert result["outcome"] == "Success"
    assert result["value"]["observationCount"] == 2
