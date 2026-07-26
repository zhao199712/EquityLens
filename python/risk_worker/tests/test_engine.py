from datetime import date, timedelta
import hashlib
from pathlib import Path

import numpy as np

from risk_worker.engine import (
    calculate_backtest,
    ewma_covariances,
    expected_shortfall,
    historical_var,
    stabilize_covariances,
)
from risk_worker.models import RiskBacktestInput


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
    assert len(first["value"]["models"]) == 6
    assert first["value"]["observationCount"] == 12


def test_shared_cross_language_fixture_is_accepted() -> None:
    fixture_path = Path(__file__).parents[3] / "tests" / "fixtures" / "risk_backtest_input.json"
    raw = fixture_path.read_text(encoding="utf-8")
    input_data = RiskBacktestInput.model_validate_json(raw)

    result = calculate_backtest(input_data, hashlib.sha256(raw.encode()).hexdigest())

    assert result["outcome"] == "Success"
    assert result["value"]["observationCount"] == 2
