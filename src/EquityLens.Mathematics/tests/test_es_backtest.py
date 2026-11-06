from datetime import date, timedelta

import numpy as np

from equitylens_mathematics.engine import calculate_backtest
from equitylens_mathematics.models import RiskBacktestInput
from equitylens_mathematics.research.es_backtest import (
    ForecastSet,
    acerbi_szekely,
    align_carry_forward,
    collect_forecasts,
    du_escanciano,
    du_escanciano_mc,
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


def _discrete(days: int = 300, atoms: int = 50, draws: int = 400, seed: int = 0) -> ForecastSet:
    """Calibrated forecasts with production-like discreteness: few distinct scenarios per day."""
    rng = np.random.default_rng(seed)
    scenarios = rng.standard_t(4, size=(days, atoms)) * 0.01 * rng.uniform(0.6, 1.6, size=(days, 1))
    samples = np.sort(scenarios[np.arange(days)[:, None], rng.integers(0, atoms, size=(days, draws))], axis=1)
    actual = samples[np.arange(days), rng.integers(0, draws, size=days)]
    return ForecastSet(actual, samples.astype(np.float32), [str(i) for i in range(days)])


def test_monte_carlo_calibrated_tests_have_nominal_size_on_discrete_fhs():
    rejections = {"z1": 0, "z2": 0, "mcneil_frey": 0, "u_es": 0, "c_es_1": 0, "c_es_5": 0}
    replications = 40
    for seed in range(replications):
        forecasts = _discrete(seed=500 + seed)
        forecasts = ForecastSet(forecasts.actual.astype(np.float32).astype(np.float64), forecasts.samples, forecasts.dates)
        az = acerbi_szekely(forecasts, 0.95, 200, f"size{seed}")
        mf = mcneil_frey(forecasts, 0.95, 200, f"size{seed}")
        de = du_escanciano_mc(forecasts, 0.95, 200, f"size{seed}")
        for key, p in (("z1", az["z1_p_left"]), ("z2", az["z2_p_left"]), ("mcneil_frey", mf["p_left"]),
                       ("u_es", de["u_es_p_right"]), ("c_es_1", de["c_es"]["1"]["p"]), ("c_es_5", de["c_es"]["5"]["p"])):
            rejections[key] += p is not None and p < 0.05
    rates = {key: value / replications for key, value in rejections.items()}
    # Nominal 5%; 40 replications give a binomial standard error of ~3.4 points.
    assert all(rate <= 0.15 for rate in rates.values()), rates


def test_mcneil_frey_null_is_not_centred_at_zero_on_discrete_fhs():
    result = mcneil_frey(_discrete(seed=3), 0.99, 300, "centre")
    assert result["null_mean"] < -0.05


def test_carry_forward_alignment_matches_production_policy():
    series = {
        "A": {"d1": 10.0, "d2": 11.0, "d3": 12.0, "d4": 13.0},
        "B": {"d0": 5.0, "d1": 5.0, "d3": 6.0, "d4": 6.5, "d5": 7.0},
    }
    names, dates, prices, filled = align_carry_forward(series)
    assert names == ["A", "B"]
    assert dates == ["d1", "d2", "d3", "d4"]
    np.testing.assert_array_equal(prices[1], [5.0, 5.0, 6.0, 6.5])
    assert filled == {"A": 0, "B": 1}


def test_history_export_loader_returns_log_returns_and_snapshot_weights(tmp_path):
    import json
    from equitylens_mathematics.research.es_backtest import load_history_export
    rows = [{"date": "2024-01-03", "previousDate": "2024-01-02", "returns": [0.1, -0.05]},
            {"date": "2024-01-05", "previousDate": "2024-01-04", "returns": [0.0, 0.02]}]
    (tmp_path / "p-returns.json").write_text(json.dumps(rows))
    (tmp_path / "p-universe.json").write_text(json.dumps({"tickers": ["A", "B"], "weights": [0.7, 0.3]}))
    tickers, dates, returns, weights = load_history_export(tmp_path, "p")
    assert tickers == ["A", "B"] and dates == ["2024-01-03", "2024-01-05"]
    np.testing.assert_allclose(np.expm1(returns), [[0.1, 0.0], [-0.05, 0.02]])
    np.testing.assert_allclose(weights, [0.7, 0.3])


def test_refit_failure_keeps_previous_fit_only_when_requested(monkeypatch):
    import pytest
    from equitylens_mathematics.garch import GarchFitError
    from equitylens_mathematics.research import es_backtest
    returns = _returns(days=100)
    dates = [str(i) for i in range(100)]
    real = es_backtest.fit_vt_garch
    calls = []

    def flaky(matrix):
        calls.append(1)
        if len(calls) == 2:
            raise GarchFitError("variance-targeted optimization failed")
        return real(matrix)

    monkeypatch.setattr(es_backtest, "fit_vt_garch", flaky)
    forecasts = es_backtest.collect_forecasts(returns, np.full(3, 1 / 3), dates, 40, 200, "f",
                                              keep_previous_on_fit_failure=True)
    assert forecasts.fit_failures == [dates[40 + 21]]
    assert forecasts.actual.size == 60
    calls.clear()
    with pytest.raises(GarchFitError):
        es_backtest.collect_forecasts(returns, np.full(3, 1 / 3), dates, 40, 200, "f")


def test_carry_forward_alignment_seeds_from_history_before_the_common_start():
    series = {
        "A": {"d2": 10.0, "d4": 11.0, "d5": 12.0},
        "B": {"d3": 5.0, "d4": 6.0, "d5": 7.0},
    }
    names, dates, prices, filled = align_carry_forward(series)
    assert dates == ["d3", "d4", "d5"]
    np.testing.assert_array_equal(prices[0], [10.0, 11.0, 12.0])
    assert filled == {"A": 1, "B": 0}
