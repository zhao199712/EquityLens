from datetime import date, timedelta

import numpy as np

from equitylens_mathematics.engine import calculate_backtest
from equitylens_mathematics.models import RiskBacktestInput
from equitylens_mathematics.research.es_backtest import (
    ForecastSet,
    acerbi_szekely,
    collect_forecasts,
    du_escanciano,
    mcneil_frey,
    z1_statistic,
    z2_statistic,
)


def _returns(seed: int = 3, assets: int = 3, days: int = 75) -> np.ndarray:
    rng = np.random.default_rng(seed)
    common = rng.standard_t(5, size=days) * 0.008
    return np.vstack([common + rng.standard_t(5, size=days) * 0.01 for _ in range(assets)])


def test_collect_forecasts_matches_production_backtest():
    returns = _returns()
    weights = np.array([0.5, 0.3, 0.2])
    dates = [(date(2024, 1, 1) + timedelta(days=i)).isoformat() for i in range(returns.shape[1])]
    portfolio = (np.exp(returns).T @ weights - weights.sum()).tolist()
    payload = RiskBacktestInput.model_validate({
        "portfolioId": "fixture", "from": dates[0], "to": dates[-1], "lookbackDays": 40,
        "simulations": 400, "confidenceLevels": [0.95, 0.99], "ewmaLambda": 0.94,
        "shrinkageAlpha": 0.1, "conservativeResidualCapQuantile": 0.99, "returnDates": dates,
        "portfolioReturns": portfolio, "assetReturns": returns.tolist(), "weights": weights.tolist(),
    })
    production = calculate_backtest(payload, "parity")
    forecasts = collect_forecasts(returns, weights, dates, 40, 400, "parity")
    for model in production["value"]["models"]:
        if model["model"] == "Historical":
            continue
        var, es = forecasts.var_es(model["confidenceLevel"])
        points = model["points"]
        assert [p["date"] for p in points] == forecasts.dates
        np.testing.assert_allclose([p["predictedVaR"] for p in points], var, rtol=1e-6, atol=1e-7)
        np.testing.assert_allclose([p["predictedES"] for p in points], es, rtol=1e-6, atol=1e-7)
        np.testing.assert_allclose([p["actualReturn"] for p in points], forecasts.actual, atol=1e-12)


def _calibrated(days: int = 600, draws: int = 2000, seed: int = 11) -> ForecastSet:
    rng = np.random.default_rng(seed)
    samples = np.sort(rng.standard_t(4, size=(days, draws)) * 0.01, axis=1).astype(np.float32)
    actual = samples[np.arange(days), rng.integers(0, draws, size=days)].astype(np.float64)
    return ForecastSet(actual, samples, [str(i) for i in range(days)])


def test_statistics_signal_underestimated_tail():
    var = np.full(4, -0.02)
    es = np.full(4, -0.03)
    worse = np.array([-0.06, 0.01, 0.0, 0.01])
    assert z1_statistic(worse, var, es) < 0
    assert z2_statistic(worse, var, es, 0.95) < 0


def test_calibrated_forecasts_have_nominal_size():
    p_values = np.array([
        acerbi_szekely(_calibrated(seed=100 + s), 0.975, repetitions=300, label="size")["z2_p_left"]
        for s in range(20)
    ])
    # 5% nominal size: 20 calibrated replications should rarely reject.
    assert np.mean(p_values < 0.05) <= 0.15
    assert 0.3 < p_values.mean() < 0.7


def test_scaled_tail_is_rejected_by_every_test():
    calibrated = _calibrated()
    result = acerbi_szekely(calibrated, 0.975, repetitions=400, label="unit")
    assert abs(result["z2_null_mean"]) < 0.1
    stressed = ForecastSet(calibrated.actual * 1.8, calibrated.samples, calibrated.dates)
    assert acerbi_szekely(stressed, 0.975, repetitions=400, label="unit")["z2_p_left"] < 0.01
    assert mcneil_frey(stressed, 0.975, repetitions=400, label="unit")["p_left"] < 0.05
    assert du_escanciano(stressed.pit(), 0.975)["u_es_p_left"] < 0.01


def test_acerbi_szekely_is_deterministic():
    calibrated = _calibrated(days=120, draws=500)
    assert acerbi_szekely(calibrated, 0.95, 200, "d") == acerbi_szekely(calibrated, 0.95, 200, "d")


def test_du_escanciano_conditional_detects_clustered_violations():
    pit = np.full(500, 0.5)
    pit[200:215] = 0.001
    assert du_escanciano(pit, 0.95)["c_es"]["1"]["p"] < 0.01
