import numpy as np

from equitylens_mathematics.research.additional_validation import (
    circular_indices,
    fz0_loss,
    mcs,
    qloss,
)


def test_circular_bootstrap_is_deterministic_and_bounded():
    first = circular_indices(17, 100, 5, "fixture")
    second = circular_indices(17, 100, 5, "fixture")
    assert np.array_equal(first, second)
    assert first.shape == (100, 17)
    assert first.min() == 0
    assert first.max() == 16


def test_quantile_loss_is_nonnegative():
    actual = np.asarray([-0.03, 0.01])
    var = np.asarray([-0.02, -0.02])
    assert np.all(qloss(actual, var, 0.95) >= 0)


def test_mcs_deterministic_and_eliminates_strictly_inferior_model():
    rng = np.random.default_rng(19)
    base = np.abs(rng.normal(0.01, 0.001, 300))
    losses = np.column_stack((base, base + 0.02, base + 0.001))
    first = mcs(losses, "unit", 0.10)
    second = mcs(losses, "unit", 0.10)
    assert first == second
    assert "VT-GARCH" not in first["members"]


def test_fz0_is_finite_and_rejects_nonnegative_es():
    actual = np.asarray([-0.03, 0.01])
    var = np.asarray([-0.02, -0.02])
    es = np.asarray([-0.025, -0.025])
    assert np.all(np.isfinite(fz0_loss(actual, var, es, 0.95)))
    with np.testing.assert_raises(ValueError):
        fz0_loss(actual, var, np.asarray([0.01, -0.02]), 0.95)
