import numpy as np

from equitylens_mathematics.engine import historical_var
from equitylens_mathematics.research.attribution import production_order_statistic_rate, unbiased_var
from equitylens_mathematics.research.es_backtest import ForecastSet


def _calibrated_scenario_days(days: int, bank: int, seed: int):
    """Each day: a fresh exchangeable bank, the production resample of it, and a new draw."""
    rng = np.random.default_rng(seed)
    banks = rng.standard_t(5, size=(days, bank))
    samples = np.sort(banks[np.arange(days)[:, None], rng.integers(0, bank, size=(days, 10_000))], axis=1)
    actual = rng.standard_t(5, size=days)
    return ForecastSet(actual, samples.astype(np.float32), [str(i) for i in range(days)])


def test_order_statistic_rate_matches_simulated_production_breach_rate():
    forecasts = _calibrated_scenario_days(6_000, 252, 1)
    for confidence in (0.95, 0.99):
        var = np.array([historical_var(row.astype(np.float64), confidence) for row in forecasts.samples])
        simulated = np.mean(forecasts.actual < var)
        expected = production_order_statistic_rate(252, confidence, repetitions=4_000)
        tolerance = 3 * np.sqrt(expected * (1 - expected) / 6_000)
        assert abs(simulated - expected) < tolerance
    assert production_order_statistic_rate(252, 0.99, repetitions=4_000) > 0.0115


def test_unbiased_var_is_close_to_nominal_under_a_correct_model():
    forecasts = _calibrated_scenario_days(6_000, 252, 2)
    rate = np.mean(forecasts.actual < unbiased_var(forecasts, 0.99))
    assert abs(rate - 0.01) < 3 * np.sqrt(0.01 * 0.99 / 6_000)
