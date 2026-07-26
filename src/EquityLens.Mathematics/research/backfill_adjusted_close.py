#!/usr/bin/env python3
"""Build deterministic adjusted-close research artifacts from a raw-price CSV.

Input columns: security_id,ticker,price_date,close.  The script performs no
database writes.  It fetches yfinance actions plus official-derived FinMind
reference events, then emits CSV/JSON/Markdown artifacts suitable for review
and explicit loading into the research-only schema.
"""

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
import csv
from datetime import date
from decimal import Decimal
import hashlib
import json
from pathlib import Path
from typing import Any

import requests
import yfinance as yf

from equitylens_mathematics.research.adjusted_close import (
    CorporateAction,
    PriceObservation,
    build_factor_chain,
    valid_log_return_pairs,
)

FACTOR_VERSION = "tw0050-total-return-v1-20260726"
FINMIND_URL = "https://api.finmindtrade.com/api/v4/data"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-csv", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--start", default="2021-07-01")
    parser.add_argument("--end", default="2026-07-27")
    parser.add_argument("--review-decisions", type=Path)
    parser.add_argument("--source-snapshot", type=Path)
    parser.add_argument("--write-source-snapshot", type=Path)
    return parser.parse_args()


def read_raw(path: Path) -> tuple[dict[str, str], dict[str, list[PriceObservation]]]:
    security_ids: dict[str, str] = {}
    prices: dict[str, list[PriceObservation]] = {}
    with path.open(newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            ticker = row["ticker"]
            security_ids[ticker] = row["security_id"]
            close = Decimal(row["close"]) if row["close"] else None
            prices.setdefault(ticker, []).append(
                PriceObservation(date.fromisoformat(row["price_date"]), close)
            )
    return security_ids, {key: sorted(value) for key, value in sorted(prices.items())}


def fetch_yfinance_actions(ticker: str, start: str, end: str) -> list[CorporateAction]:
    history = yf.Ticker(f"{ticker}.TW").history(
        start=start,
        end=end,
        auto_adjust=False,
        actions=True,
        repair=False,
        keepna=True,
    )
    actions: list[CorporateAction] = []
    for timestamp, row in history.iterrows():
        event_date = timestamp.date()
        dividend = row.get("Dividends", 0)
        split = row.get("Stock Splits", 0)
        if dividend == dividend and float(dividend) != 0:
            actions.append(
                CorporateAction(
                    event_date,
                    "dividend",
                    Decimal(str(float(dividend))),
                    source_id=f"yf:{ticker}:dividend:{event_date}",
                )
            )
        if split == split and float(split) != 0:
            actions.append(
                CorporateAction(
                    event_date,
                    "split",
                    Decimal(str(float(split))),
                    source_id=f"yf:{ticker}:split:{event_date}",
                )
            )
    return sorted(actions)


def fetch_finmind(ticker: str, start: str, end: str) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for dataset in (
        "TaiwanStockDividendResult",
        "TaiwanStockCapitalReductionReferencePrice",
        "TaiwanStockSplitPrice",
    ):
        response = requests.get(
            FINMIND_URL,
            params={
                "dataset": dataset,
                "data_id": ticker,
                "start_date": start,
                "end_date": end,
            },
            timeout=45,
        )
        response.raise_for_status()
        payload = response.json()
        if payload.get("status") != 200:
            raise RuntimeError(f"{ticker} {dataset}: {payload.get('msg')}")
        for row in payload.get("data", []):
            row = dict(row)
            row["_dataset"] = dataset
            result.append(row)
    return sorted(result, key=lambda row: (row["date"], row["_dataset"]))


def fetch_twse_results(start: str, end: str) -> dict[str, list[dict[str, Any]]]:
    """Fetch the official five-year TWT49U calculation-result table once."""
    response = requests.get(
        "https://www.twse.com.tw/exchangeReport/TWT49U",
        params={
            "response": "json",
            "startDate": start.replace("-", ""),
            "endDate": end.replace("-", ""),
        },
        timeout=90,
    )
    response.raise_for_status()
    payload = response.json()
    if payload.get("stat") != "OK":
        raise RuntimeError(f"TWSE TWT49U: {payload.get('stat')}")
    result: dict[str, list[dict[str, Any]]] = {}
    for values in payload.get("data", []):
        detail = values[11].split(",")
        if len(detail) != 2:
            continue
        ticker, yyyymmdd = detail
        row = {
            "date": f"{yyyymmdd[:4]}-{yyyymmdd[4:6]}-{yyyymmdd[6:]}",
            "stock_id": ticker,
            "before_price": values[3].replace(",", ""),
            "after_price": values[4].replace(",", ""),
            "value": values[5],
            "kind": values[6],
            "_dataset": "TWSE_TWT49U",
            "_raw": values,
        }
        result.setdefault(ticker, []).append(row)
    return result


def official_factor(row: dict[str, Any]) -> Decimal | None:
    before = row.get("before_price")
    after = row.get("after_price") or row.get("reference_price")
    if before in (None, 0) or after is None:
        return None
    return Decimal(str(after)) / Decimal(str(before))


def reconcile(
    ticker: str,
    chain: Any,
    official: list[dict[str, Any]],
    reviewed: dict[tuple[str, str], str],
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    grouped: dict[date, list[Any]] = {}
    for decision in chain.decisions:
        grouped.setdefault(decision.event_date, []).append(decision)
    for event_date, decisions in sorted(grouped.items()):
        action_types = sorted(item.action_type for item in decisions)
        combined_factor = Decimal(1)
        for item in decisions:
            combined_factor *= item.event_factor
        candidates = []
        for row in official:
            dataset = row["_dataset"]
            same_kind = dataset == "TWSE_TWT49U" or (
                "dividend" in action_types
                and dataset in {"TWSE_TWT49U", "TaiwanStockDividendResult"}
            ) or (
                not ("dividend" in action_types)
                and any(kind in {"split", "capital_reduction"} for kind in action_types)
                and dataset
                in {
                    "TaiwanStockCapitalReductionReferencePrice",
                    "TaiwanStockSplitPrice",
                }
            )
            distance = abs(
                (date.fromisoformat(row["date"]) - event_date).days
            )
            limit = 3 if "dividend" in action_types else 14
            if same_kind and distance <= limit:
                priority = 0 if dataset == "TWSE_TWT49U" else 1
                candidates.append((distance, priority, row))
        matched = min(candidates, default=(None, None, None), key=lambda item: item[:2])[2]
        official_value = official_factor(matched) if matched else None
        difference = (
            abs(combined_factor - official_value) / abs(official_value)
            if official_value not in (None, 0)
            else None
        )
        if matched is None:
            status = "manual_review"
            conclusion = "No historical official-derived reference event matched"
        elif difference is not None and difference > Decimal("0.001"):
            if reviewed.get((ticker, event_date.isoformat())) == "use_official":
                status = "accepted_official_override"
                conclusion = (
                    "Human-reviewed override: use authoritative official "
                    "reference factor; retain yfinance discrepancy in audit row"
                )
            else:
                status = "manual_review"
                conclusion = "Relative factor difference exceeds 0.1%; no auto repair"
        else:
            status = "accepted"
            conclusion = "Use yfinance action; official reference factor agrees within 0.1%"
        rows.append(
            {
                "ticker": ticker,
                "event_date": event_date.isoformat(),
                "action_type": "+".join(action_types),
                "action_values": json.dumps(
                    [
                        {
                            "type": item.action_type,
                            "value": format(item.value, "f"),
                            "sourceId": item.source_id,
                        }
                        for item in decisions
                    ],
                    ensure_ascii=False,
                    sort_keys=True,
                ),
                "yfinance_factor": format(combined_factor, "f"),
                "official_date": matched["date"] if matched else "",
                "official_factor": (
                    format(official_value, "f") if official_value is not None else ""
                ),
                "relative_difference": (
                    format(difference, "f") if difference is not None else ""
                ),
                "reconciliation_status": status,
                "conclusion": conclusion,
                "source_payload": json.dumps(
                    matched or {}, ensure_ascii=False, sort_keys=True
                ),
                "factor_version": FACTOR_VERSION,
            }
        )
    return rows


def write_csv(path: Path, rows: list[dict[str, Any]], fields: list[str]) -> None:
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def load_source_snapshot(
    path: Path,
) -> dict[str, tuple[list[CorporateAction], list[dict[str, Any]]]]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    result = {}
    for ticker, source in sorted(payload["tickers"].items()):
        actions = [
            CorporateAction(
                date.fromisoformat(row["event_date"]),
                row["action_type"],
                Decimal(row["value"]),
                source=row.get("source", "yfinance"),
                source_id=row.get("source_id"),
            )
            for row in source["actions"]
        ]
        result[ticker] = (actions, source["official"])
    return result


def write_source_snapshot(
    path: Path,
    sources: dict[str, tuple[list[CorporateAction], list[dict[str, Any]]]],
) -> None:
    payload = {
        "factorVersion": FACTOR_VERSION,
        "tickers": {
            ticker: {
                "actions": [
                    {
                        "event_date": item.event_date.isoformat(),
                        "action_type": item.action_type,
                        "value": format(item.value, "f"),
                        "source": item.source,
                        "source_id": item.source_id,
                    }
                    for item in actions
                ],
                "official": official,
            }
            for ticker, (actions, official) in sorted(sources.items())
        },
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def main() -> None:
    args = parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    security_ids, prices = read_raw(args.raw_csv)
    all_price_rows: list[dict[str, Any]] = []
    all_action_rows: list[dict[str, Any]] = []
    all_gap_rows: list[dict[str, Any]] = []
    anomalies: list[dict[str, Any]] = []
    reviewed: dict[tuple[str, str], str] = {}
    if args.review_decisions:
        with args.review_decisions.open(newline="", encoding="utf-8") as handle:
            for row in csv.DictReader(handle):
                reviewed[(row["ticker"], row["event_date"])] = row["decision"]

    common_dates: dict[date, int] = {}
    for observations in prices.values():
        for observation in observations:
            common_dates[observation.price_date] = (
                common_dates.get(observation.price_date, 0) + 1
            )
    expected_dates = {
        day for day, count in common_dates.items() if count >= len(prices) * 0.9
    }

    def fetch_sources(ticker: str) -> tuple[list[CorporateAction], list[dict[str, Any]]]:
        return (
            fetch_yfinance_actions(ticker, args.start, args.end),
            fetch_finmind(ticker, args.start, args.end),
        )

    if args.source_snapshot:
        source_rows = load_source_snapshot(args.source_snapshot)
    else:
        with ThreadPoolExecutor(max_workers=5) as executor:
            fetched = dict(
                zip(prices, executor.map(fetch_sources, prices, timeout=600))
            )
        twse_results = fetch_twse_results(args.start, args.end)
        source_rows = {
            ticker: (actions, official + twse_results.get(ticker, []))
            for ticker, (actions, official) in fetched.items()
        }
    if args.write_source_snapshot:
        write_source_snapshot(args.write_source_snapshot, source_rows)

    for ticker, observations in prices.items():
        actions, official = source_rows[ticker]
        candidate_chain = build_factor_chain(
            observations, actions, factor_version=FACTOR_VERSION
        )
        reconciliation = reconcile(
            ticker, candidate_chain, official, reviewed
        )
        all_action_rows.extend(reconciliation)
        override_by_date = {
            date.fromisoformat(row["event_date"]): Decimal(row["official_factor"])
            for row in reconciliation
            if row["reconciliation_status"] == "accepted_official_override"
        }
        final_actions = [
            action for action in actions if action.event_date not in override_by_date
        ]
        final_actions.extend(
            CorporateAction(
                event_date,
                "official_factor",
                factor,
                source="TWSE-reviewed",
                source_id=f"review:{ticker}:{event_date}",
            )
            for event_date, factor in override_by_date.items()
        )
        chain = build_factor_chain(
            observations, final_actions, factor_version=FACTOR_VERSION
        )

        decisions = [
            {
                "eventDate": item.event_date.isoformat(),
                "actionType": item.action_type,
                "eventFactor": format(item.event_factor, "f"),
                "sourceId": item.source_id,
                "rationale": item.rationale,
            }
            for item in chain.decisions
        ]
        for row in chain.rows:
            all_price_rows.append(
                {
                    "security_id": security_ids[ticker],
                    "ticker": ticker,
                    "price_date": row.price_date.isoformat(),
                    "raw_close": (
                        format(row.raw_close, "f")
                        if row.raw_close is not None
                        else ""
                    ),
                    "adjusted_close": (
                        format(row.adjusted_close, "f")
                        if row.adjusted_close is not None
                        else ""
                    ),
                    "adj_factor": (
                        format(row.adj_factor, "f")
                        if row.adj_factor is not None
                        else ""
                    ),
                    "factor_source": row.factor_source,
                    "factor_version": row.factor_version,
                    "status": row.status,
                    "status_reason": row.status_reason or "",
                    "decision_json": json.dumps(
                        {
                            "factorChain": "risk_research.corporate_action",
                            "factorVersion": FACTOR_VERSION,
                        },
                        sort_keys=True,
                    ),
                }
            )
            if row.status != "adjusted":
                all_gap_rows.append(
                    {
                        "ticker": ticker,
                        "price_date": row.price_date.isoformat(),
                        "reason": row.status_reason,
                        "source": "production-market_price-read-only",
                        "factor_version": FACTOR_VERSION,
                    }
                )
        present = {item.price_date for item in observations}
        for missing in sorted(expected_dates - present):
            all_gap_rows.append(
                {
                    "ticker": ticker,
                    "price_date": missing.isoformat(),
                    "reason": "absent while at least 90% of universe traded",
                    "source": "cross-sectional-calendar",
                    "factor_version": FACTOR_VERSION,
                }
            )
        for prior_date, current_date, value in valid_log_return_pairs(chain.rows):
            if abs(value) > 0.25:
                related = [
                    item
                    for item in decisions
                    if prior_date.isoformat()
                    <= item["eventDate"]
                    <= current_date.isoformat()
                ]
                anomalies.append(
                    {
                        "ticker": ticker,
                        "prior_date": prior_date.isoformat(),
                        "price_date": current_date.isoformat(),
                        "log_return": format(value, ".12f"),
                        "attribution": (
                            "corporate_action:" + ",".join(
                                item["actionType"] for item in related
                            )
                            if related
                            else "manual_review:market_move_or_unmatched_event"
                        ),
                    }
                )

    fields = [
        "security_id",
        "ticker",
        "price_date",
        "raw_close",
        "adjusted_close",
        "adj_factor",
        "factor_source",
        "factor_version",
        "status",
        "status_reason",
        "decision_json",
    ]
    write_csv(args.output_dir / "price_adjustment.csv", all_price_rows, fields)
    action_fields = [
        "ticker",
        "event_date",
        "action_type",
        "action_values",
        "yfinance_factor",
        "official_date",
        "official_factor",
        "relative_difference",
        "reconciliation_status",
        "conclusion",
        "source_payload",
        "factor_version",
    ]
    write_csv(
        args.output_dir / "corporate_action_reconciliation.csv",
        all_action_rows,
        action_fields,
    )
    gap_fields = ["ticker", "price_date", "reason", "source", "factor_version"]
    write_csv(args.output_dir / "price_gap.csv", all_gap_rows, gap_fields)
    anomaly_fields = [
        "ticker",
        "prior_date",
        "price_date",
        "log_return",
        "attribution",
    ]
    write_csv(args.output_dir / "return_anomalies.csv", anomalies, anomaly_fields)

    artifacts = sorted(args.output_dir.glob("*.csv"))
    checksums = {
        path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in artifacts
    }
    accepted = sum(
        item["reconciliation_status"].startswith("accepted")
        for item in all_action_rows
    )
    overrides = sum(
        item["reconciliation_status"] == "accepted_official_override"
        for item in all_action_rows
    )
    manual = sum(
        item["reconciliation_status"] == "manual_review"
        for item in all_action_rows
    )
    coverage = sum(item["status"] == "adjusted" for item in all_price_rows)
    summary = {
        "factorVersion": FACTOR_VERSION,
        "dataAsOf": max(
            item.price_date for rows in prices.values() for item in rows
        ).isoformat(),
        "tickers": sorted(prices),
        "rawRows": len(all_price_rows),
        "adjustedRows": coverage,
        "labeledInvalidRows": len(all_price_rows) - coverage,
        "labeledCalendarGaps": len(all_gap_rows),
        "corporateActions": len(all_action_rows),
        "acceptedReconciliations": accepted,
        "reviewedOfficialOverrides": overrides,
        "manualReconciliations": manual,
        "extremeAdjustedReturns": len(anomalies),
        "checksums": checksums,
        "officialSourcePolicy": (
            "TWSE TWT49U is the primary five-year dividend/right reference. "
            "FinMind's TWSE-derived CapitalReduction/Split rows supplement event "
            "types explicitly excluded by TWT49U; every source row is retained."
        ),
    }
    (args.output_dir / "backfill-summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(summary, ensure_ascii=False, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
