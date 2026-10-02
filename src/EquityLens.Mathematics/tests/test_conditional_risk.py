import json
import math
from dataclasses import replace

import numpy as np
import pytest
from pydantic import ValidationError

from equitylens_mathematics.conditional_risk import conditional_risk
from equitylens_mathematics.garch import GarchFitError, VtGarchFit
from equitylens_mathematics.models import RiskAsset, RiskBacktestInput
from test_engine import fixture_input


def metadata(n, industries=None):
    return [RiskAsset(securityId=str(i), ticker=str(i), exchange="TWSE", securityName=f"Asset {i}",
                      industry=industries[i] if industries else "科技") for i in range(n)]


def fitted(residuals, variances):
    return VtGarchFit((), np.asarray(residuals, dtype=float), np.asarray(variances, dtype=float), 0)


def test_single_asset_units_and_cash_removal():
    result = conditional_risk(fitted([[-1], [0], [1]], [4]), np.array([0.6]), metadata(1))
    expected = 0.6 * 0.02 * math.sqrt(252)
    assert result["dailyVolatility"] == pytest.approx(0.012)
    assert result["annualizedVolatility"] == pytest.approx(expected)
    row = result["holdings"][0]
    assert row["componentVolatility"] == pytest.approx(expected)
    assert row["componentRiskShare"] == pytest.approx(1)
    assert row["marginalVolatility"] == pytest.approx(0.02 * math.sqrt(252))
    assert row["incrementalVolatility"] == pytest.approx(expected)
    assert row["annualizedVolatility"] == pytest.approx(0.02 * math.sqrt(252))


def test_euler_sum_derivatives_industry_proportions_and_removal():
    rng = np.random.default_rng(12)
    z = rng.normal(size=(80, 3))
    z[:, 1] = -0.8 * z[:, 0] + 0.2 * z[:, 1]
    fit = fitted(z, [4, 1, 2])
    w = np.array([0.6, 0.1, 0.3])
    assets = metadata(3, ["科技", "金融", "科技"])
    result = conditional_risk(fit, w, assets)
    rows = result["holdings"]
    assert rows[1]["componentRiskShare"] < 0
    assert rows[1]["incrementalVolatility"] < 0
    for table in (rows, result["industries"]):
        assert sum(row["componentVolatility"] for row in table) == pytest.approx(result["annualizedVolatility"])
        assert sum(row["componentRiskShare"] for row in table) == pytest.approx(1)
    d = np.sqrt([4, 1, 2]) / 100
    cov = np.corrcoef(z, rowvar=False) * np.outer(d, d)
    sigma = lambda weights: math.sqrt(max(0, weights @ cov @ weights)) * math.sqrt(252)
    assert result["annualizedVolatility"] == pytest.approx(sigma(w))
    eps = 1e-6
    for i, row in enumerate(rows):
        direction = np.eye(3)[i]
        finite_difference = (sigma(w + eps * direction) - sigma(w - eps * direction)) / (2 * eps)
        assert row["marginalVolatility"] == pytest.approx(finite_difference, rel=1e-7)
        without = w.copy()
        without[i] = 0
        assert row["incrementalVolatility"] == pytest.approx(sigma(w) - sigma(without))
    for industry in result["industries"]:
        indices = [i for i, asset in enumerate(assets) if asset.industry == industry["industry"]]
        direction = np.zeros(3)
        direction[indices] = w[indices] / sum(w[indices])
        finite_difference = (sigma(w + eps * direction) - sigma(w - eps * direction)) / (2 * eps)
        assert industry["marginalVolatility"] == pytest.approx(finite_difference, rel=1e-7)
        without = w.copy()
        without[indices] = 0
        assert industry["incrementalVolatility"] == pytest.approx(sigma(w) - sigma(without))


def test_singular_correlation_and_zero_portfolio_variance():
    fit = fitted([[-1, -1], [0, 0], [1, 1]], [1, 1])
    assert conditional_risk(fit, np.array([0.5, 0.5]))["dailyVolatility"] == pytest.approx(0.01)
    hedge = replace(fit, residuals=fit.residuals * [1, -1])
    result = conditional_risk(hedge, np.array([0.5, 0.5]), metadata(2))
    assert result["annualizedVolatility"] == 0
    for row in result["holdings"] + result["industries"]:
        assert row["componentVolatility"] is None
        assert row["componentRiskShare"] is None
        assert row["marginalVolatility"] is None
    json.dumps(result, allow_nan=False)


def test_zero_weight_individual_has_marginal_but_empty_industry_has_no_direction():
    result = conditional_risk(fitted([[-1, -1], [0, 0], [1, 1]], [1, 4]),
                              np.array([1, 0]), metadata(2, ["科技", "金融"]))
    assert result["holdings"][1]["componentVolatility"] == 0
    assert result["holdings"][1]["marginalVolatility"] == pytest.approx(.02 * math.sqrt(252))
    assert result["industries"][1]["marginalVolatility"] is None


@pytest.mark.parametrize("residuals,variances,weights", [
    ([[0], [0]], [1], [1]), ([[float('nan')], [1]], [1], [1]),
    ([[-1], [1]], [float('inf')], [1]), ([[-1], [1]], [0], [1]),
    ([[-1], [1]], [1], [float('nan')]), ([[-1], [1]], [1], [-1]),
    ([[-1], [1]], [1, 1], [1]), ([[1]], [1], [1]),
])
def test_invalid_inputs_fail_without_silent_covariance_repair(residuals, variances, weights):
    with pytest.raises(GarchFitError):
        conditional_risk(fitted(residuals, variances), np.array(weights))


def test_legacy_metadata_optional_and_new_metadata_roundtrips():
    original = fixture_input()
    assert original.assets is None
    result = conditional_risk(fitted([[-1, 1], [1, 0], [0, -1]], [1, 2]), np.array(original.weights))
    assert result["annualizedVolatility"] > 0
    assert result["holdings"] == result["industries"] == []
    data = original.model_dump(by_alias=True)
    data["assets"] = [asset.model_dump(by_alias=True) for asset in metadata(2)]
    parsed = RiskBacktestInput.model_validate(data)
    assert RiskBacktestInput.model_validate_json(parsed.model_dump_json(by_alias=True)).assets == parsed.assets
    data["assets"][1]["securityId"] = "0"
    with pytest.raises(ValidationError, match="unique"):
        RiskBacktestInput.model_validate(data)
    data["assets"] = data["assets"][:1]
    with pytest.raises(ValidationError, match="equal lengths"):
        RiskBacktestInput.model_validate(data)


@pytest.mark.parametrize('operation', ['risk', 'monte-carlo'])
def test_engine_reuses_fit_and_preserves_historical_metric(monkeypatch, operation):
    from equitylens_mathematics import engine
    from equitylens_mathematics.garch import PathDistribution, VtGarchParameters
    data = fixture_input().model_copy(update={"assets": metadata(2)})
    fit = fitted([[-1, 1], [1, 0], [0, -1]], [1, 2])
    fit = replace(fit, parameters=(VtGarchParameters(.1, .1, .8, 8, .9, 1),) * 2)
    calls = []
    monkeypatch.setattr(engine, 'fit_vt_garch', lambda matrix: calls.append(matrix) or fit)
    monkeypatch.setattr(engine, 'simulate_paths', lambda *args: PathDistribution(
        {1: np.array([-.01, .01]), 252: np.array([-.1, .1])}, np.zeros((1, 2)), np.zeros((2, 5))))
    value = engine.calculate_current_risk(data, 'b' * 64, operation)['value']
    assert len(calls) == 1
    assert value['conditionalRisk']['holdings'][0]['securityId'] == '0'
    assert value['historicalAnnualizedVolatility'] == pytest.approx(np.std(data.portfolio_returns, ddof=1) * math.sqrt(252))
    assert value['conditionalRisk']['annualizedVolatility'] != pytest.approx(value['historicalAnnualizedVolatility'])
