import numpy as np

from equitylens_mathematics.garch import fit_vt_garch, one_day_distribution
from equitylens_mathematics.research.fhs_tail import (
    XI_MAX,
    fit_lower_tails,
    one_day_distribution_evt,
)


def _fit(assets: int = 1, seed: int = 5):
    rng = np.random.default_rng(seed)
    return fit_vt_garch(rng.standard_t(4, size=(assets, 252)) * 0.012)


def test_tail_model_thresholds_and_mean_excess():
    residuals = np.random.default_rng(1).standard_t(4, size=(252, 3))
    model = fit_lower_tails(residuals, 0.10)
    ordered = np.sort(residuals, axis=0)
    assert model.tail_count == 25
    np.testing.assert_allclose(model.thresholds, ordered[25])
    assert 0.0 <= model.xi <= XI_MAX
    mean_excess = (ordered[25] - ordered[:25]).mean(axis=0)
    np.testing.assert_allclose(model.scales / (1 - model.xi), mean_excess)


def test_body_matches_production_and_tail_extends_beyond_history():
    fit = _fit()
    weights = np.array([1.0])
    base = one_day_distribution(fit, weights, 20_000, 42)
    evt = one_day_distribution_evt(fit, weights, 20_000, 42, 0.10)
    model = fit_lower_tails(fit.residuals, 0.10)
    sd = np.sqrt(fit.next_variances[0]) / 100.0
    threshold_return = np.exp(model.thresholds[0] * sd) - 1.0
    body = base > threshold_return
    np.testing.assert_array_equal(evt[body], base[body])
    assert np.all(evt[~body] <= threshold_return + 1e-15)
    assert evt.min() < np.exp(fit.residuals.min() * sd) - 1.0


def test_evt_sampler_is_deterministic():
    fit = _fit(assets=3)
    weights = np.array([0.5, 0.3, 0.2])
    first = one_day_distribution_evt(fit, weights, 2_000, 9)
    second = one_day_distribution_evt(fit, weights, 2_000, 9)
    np.testing.assert_array_equal(first, second)
