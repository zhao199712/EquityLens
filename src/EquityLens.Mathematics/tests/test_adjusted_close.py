from datetime import date
from decimal import Decimal
import math

from equitylens_mathematics.research.adjusted_close import (
    CorporateAction,
    PriceObservation,
    build_factor_chain,
    valid_log_return_pairs,
)


def test_2327_split_removes_false_738_percent_return() -> None:
    chain = build_factor_chain(
        [
            PriceObservation(date(2025, 8, 13), Decimal("546")),
            PriceObservation(date(2025, 8, 25), Decimal("143")),
        ],
        [
            CorporateAction(
                date(2025, 8, 14),
                "split",
                Decimal("4"),
                source_id="2327-2025-face-value-change",
            )
        ],
        factor_version="fixture-v1",
    )

    raw_return = math.log(143 / 546)
    adjusted_return = valid_log_return_pairs(chain.rows)[0][2]
    assert abs(raw_return - (-1.3398)) < 0.001
    assert abs(adjusted_return) <= 0.20
    assert any(
        decision.action_type == "split" and decision.event_date == date(2025, 8, 14)
        for decision in chain.decisions
    )


def test_2887_zero_is_missing_and_no_return_bridges_it() -> None:
    chain = build_factor_chain(
        [
            PriceObservation(date(2024, 8, 21), Decimal("26.4")),
            PriceObservation(date(2024, 8, 22), Decimal("0")),
            PriceObservation(date(2024, 8, 23), Decimal("26.7")),
        ],
        [],
        factor_version="fixture-v1",
    )

    assert chain.rows[1].status == "invalid_raw"
    assert chain.rows[1].adjusted_close is None
    assert valid_log_return_pairs(chain.rows) == ()


def test_2317_missing_day_excludes_both_adjacent_return_intervals() -> None:
    chain = build_factor_chain(
        [
            PriceObservation(date(2025, 7, 29), Decimal("170")),
            PriceObservation(date(2025, 7, 30), None),
            PriceObservation(date(2025, 7, 31), Decimal("168")),
        ],
        [],
        factor_version="fixture-v1",
    )

    assert chain.rows[1].status == "missing"
    assert valid_log_return_pairs(chain.rows) == ()


def test_factor_chain_serialization_is_bit_reproducible() -> None:
    inputs = [
        PriceObservation(date(2025, 1, 1), Decimal("100")),
        PriceObservation(date(2025, 1, 2), Decimal("98")),
    ]
    actions = [
        CorporateAction(date(2025, 1, 2), "dividend", Decimal("2"))
    ]
    first = build_factor_chain(inputs, actions, factor_version="fixture-v1")
    second = build_factor_chain(inputs, actions, factor_version="fixture-v1")
    assert first.serializable() == second.serializable()


def test_reviewed_official_factor_is_applied_exactly() -> None:
    chain = build_factor_chain(
        [
            PriceObservation(date(2025, 1, 1), Decimal("100")),
            PriceObservation(date(2025, 1, 2), Decimal("96")),
        ],
        [
            CorporateAction(
                date(2025, 1, 2),
                "official_factor",
                Decimal("0.96"),
                source="TWSE-reviewed",
            )
        ],
        factor_version="fixture-v1",
    )
    assert chain.rows[0].adjusted_close == Decimal("96.000000000000")
    assert chain.decisions[0].source == "TWSE-reviewed"
