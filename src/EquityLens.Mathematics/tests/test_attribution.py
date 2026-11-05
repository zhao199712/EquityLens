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


def test_exchangeable_u_es_has_nominal_size_where_model_own_null_does_not():
    from equitylens_mathematics.research.es_backtest import du_escanciano_exchangeable, du_escanciano_mc
    exchangeable, model_own = [], []
    for seed in range(20):
        forecasts = _calibrated_scenario_days(1_500, 252, 700 + seed)
        exchangeable.append(du_escanciano_exchangeable(forecasts, 0.975, 300, f"s{seed}")["u_es_p_right"])
        model_own.append(du_escanciano_mc(forecasts, 0.975, 300, f"s{seed}")["u_es_p_right"])
    assert np.mean(np.asarray(exchangeable) < 0.05) <= 0.15
    # Documented failure mode: the model-own null ignores finite-bank estimation error.
    assert np.mean(np.asarray(model_own) < 0.05) > 0.3
