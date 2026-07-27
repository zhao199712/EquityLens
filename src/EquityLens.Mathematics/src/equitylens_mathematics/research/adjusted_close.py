"""Traceable backward-adjustment factor chains.

Raw observations are never changed.  Invalid or absent closes remain gaps, and
no interpolation or inferred corporate action is permitted.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass
from datetime import date
from decimal import Decimal, ROUND_HALF_EVEN, getcontext
from typing import Iterable

getcontext().prec = 34
_QUANTUM = Decimal("0.000000000001")


def _decimal(value: Decimal | int | float | str) -> Decimal:
    return value if isinstance(value, Decimal) else Decimal(str(value))


def _quantize(value: Decimal) -> Decimal:
    return value.quantize(_QUANTUM, rounding=ROUND_HALF_EVEN)


@dataclass(frozen=True, order=True)
class PriceObservation:
    price_date: date
    close: Decimal | None


@dataclass(frozen=True, order=True)
class CorporateAction:
    event_date: date
    action_type: str
    value: Decimal
    source: str = "yfinance"
    source_id: str | None = None


@dataclass(frozen=True)
class AdjustmentDecision:
    event_date: date
    action_type: str
    value: Decimal
    event_factor: Decimal
    source: str
    source_id: str | None
    rationale: str


@dataclass(frozen=True)
class AdjustedObservation:
    price_date: date
    raw_close: Decimal | None
    adjusted_close: Decimal | None
    adj_factor: Decimal | None
    status: str
    status_reason: str | None
    factor_source: str
    factor_version: str


@dataclass(frozen=True)
class FactorChain:
    rows: tuple[AdjustedObservation, ...]
    decisions: tuple[AdjustmentDecision, ...]
    manual_review: tuple[str, ...]

    def serializable(self) -> dict:
        def convert(value: object) -> object:
            if isinstance(value, Decimal):
                return format(value, "f")
            if isinstance(value, date):
                return value.isoformat()
            if isinstance(value, tuple):
                return [convert(item) for item in value]
            if isinstance(value, dict):
                return {key: convert(item) for key, item in value.items()}
            return value

        return convert(
            {
                "rows": [asdict(row) for row in self.rows],
                "decisions": [asdict(row) for row in self.decisions],
                "manualReview": list(self.manual_review),
            }
        )


def build_factor_chain(
    prices: Iterable[PriceObservation],
    actions: Iterable[CorporateAction],
    *,
    factor_version: str,
) -> FactorChain:
    """Build an auditable total-return adjustment chain.

    A split ratio R applies a backward factor of 1/R.  A cash dividend D on
    ex-date t applies (P[t-1] - D) / P[t-1] to observations before t.  This is
    algebraically equivalent to removing the mechanical ex-dividend drop from
    a raw-close return.  The ``P/(P+D)`` expression sometimes used in vendor
    documentation assumes P is the post-event reference price; the expression
    here uses the observed pre-event raw close.
    """

    ordered_prices = sorted(prices, key=lambda item: item.price_date)
    if not ordered_prices:
        return FactorChain((), (), ())
    by_date = {item.price_date: item for item in ordered_prices}
    valid = [
        item
        for item in ordered_prices
        if item.close is not None and _decimal(item.close) > 0
    ]
    decisions: list[AdjustmentDecision] = []
    reviews: list[str] = []
    event_factors: list[tuple[date, Decimal]] = []

    for action in sorted(actions, key=lambda item: (item.event_date, item.action_type)):
        value = _decimal(action.value)
        if action.action_type == "official_factor":
            if value <= 0:
                reviews.append(
                    f"{action.event_date}:official_factor:non-positive factor {value}"
                )
                continue
            factor = value
            rationale = "explicitly reviewed official reference factor override"
        elif action.action_type in {"split", "capital_reduction"}:
            if value <= 0:
                reviews.append(
                    f"{action.event_date}:{action.action_type}:non-positive ratio {value}"
                )
                continue
            factor = Decimal(1) / value
            rationale = "backward factor 1/ratio; economic value is continuous"
        elif action.action_type == "dividend":
            prior = next(
                (
                    item
                    for item in reversed(valid)
                    if item.price_date < action.event_date
                ),
                None,
            )
            if prior is None or prior.close is None:
                reviews.append(
                    f"{action.event_date}:dividend:no prior valid raw close"
                )
                continue
            prior_close = _decimal(prior.close)
            if value < 0 or value >= prior_close:
                reviews.append(
                    f"{action.event_date}:dividend:invalid dividend {value} "
                    f"for prior close {prior_close}"
                )
                continue
            factor = (prior_close - value) / prior_close
            rationale = (
                "total-return adjustment using (pre-ex close - dividend) / "
                "pre-ex close"
            )
        else:
            reviews.append(
                f"{action.event_date}:{action.action_type}:unsupported action"
            )
            continue

        factor = _quantize(factor)
        event_factors.append((action.event_date, factor))
        decisions.append(
            AdjustmentDecision(
                event_date=action.event_date,
                action_type=action.action_type,
                value=value,
                event_factor=factor,
                source=action.source,
                source_id=action.source_id,
                rationale=rationale,
            )
        )

    rows: list[AdjustedObservation] = []
    factor_source = "yfinance-actions+official-reconciliation"
    for observation in ordered_prices:
        if observation.close is None:
            rows.append(
                AdjustedObservation(
                    observation.price_date,
                    None,
                    None,
                    None,
                    "missing",
                    "raw observation absent",
                    factor_source,
                    factor_version,
                )
            )
            continue
        raw = _decimal(observation.close)
        if raw <= 0:
            rows.append(
                AdjustedObservation(
                    observation.price_date,
                    raw,
                    None,
                    None,
                    "invalid_raw",
                    "raw close is non-positive",
                    factor_source,
                    factor_version,
                )
            )
            continue
        factor = Decimal(1)
        for event_date, event_factor in event_factors:
            if observation.price_date < event_date:
                factor *= event_factor
        factor = _quantize(factor)
        rows.append(
            AdjustedObservation(
                observation.price_date,
                raw,
                _quantize(raw * factor),
                factor,
                "adjusted",
                None,
                factor_source,
                factor_version,
            )
        )

    return FactorChain(tuple(rows), tuple(decisions), tuple(reviews))


def valid_log_return_pairs(
    rows: Iterable[AdjustedObservation],
) -> tuple[tuple[date, date, float], ...]:
    """Return consecutive valid log-return pairs without bridging any gap."""
    import math

    ordered = sorted(rows, key=lambda item: item.price_date)
    result: list[tuple[date, date, float]] = []
    for previous, current in zip(ordered, ordered[1:]):
        if previous.adjusted_close is None or current.adjusted_close is None:
            continue
        result.append(
            (
                previous.price_date,
                current.price_date,
                math.log(float(current.adjusted_close / previous.adjusted_close)),
            )
        )
    return tuple(result)
