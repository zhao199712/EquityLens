"""Shared acceptance examples for the API and research factor chain."""
from datetime import date
from decimal import Decimal, ROUND_HALF_EVEN
import json
from pathlib import Path

from equitylens_mathematics.research.adjusted_close import CorporateAction, PriceObservation, build_factor_chain


def test_api_golden_fixtures_match_research_at_database_precision():
    fixture = Path(__file__).resolve().parents[3] / "tests/EquityLens.Api.Tests/Fixtures/price-adjustment-chain.json"
    cases = json.loads(fixture.read_text(), parse_float=Decimal, parse_int=Decimal)
    for case in cases:
        prices = [PriceObservation(date.fromisoformat(day), Decimal(close)) for day, close in case["prices"]]
        actions = []
        for day, kind, value, factor in case["actions"]:
            if factor is not None:
                kind, value = "official_factor", factor
            elif kind == "cash":
                kind = "dividend"
            actions.append(CorporateAction(date.fromisoformat(day), kind, Decimal(value)))
        chain = build_factor_chain(prices, actions, factor_version="tw-full-series-v1")
        assert not chain.manual_review, case["name"]
        actual = [row.adjusted_close.quantize(Decimal("0.000001"), rounding=ROUND_HALF_EVEN) for row in chain.rows]
        assert actual == case["expected"], case["name"]
