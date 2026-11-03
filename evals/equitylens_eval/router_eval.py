"""Deterministic routing evaluation; reads captured decisions, never calls an LLM.

Prediction JSONL: {id, action, workflow, params}. Input messages and scenario
context belong in the cases file; prediction records must not contain secrets.
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
from typing import Any


def read_jsonl(path: str | Path) -> list[dict[str, Any]]:
    values = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    ids = [value['id'] for value in values]
    if len(ids) != len(set(ids)):
        raise ValueError('Duplicate case/prediction IDs')
    return values


def contains(actual: Any, expected: Any) -> bool:
    if isinstance(expected, dict):
        return isinstance(actual, dict) and all(key in actual and contains(actual[key], value) for key, value in expected.items())
    if isinstance(expected, list):
        return isinstance(actual, list) and len(actual) == len(expected) and all(contains(a, e) for a, e in zip(actual, expected))
    return type(actual) is type(expected) and actual == expected or (
        type(actual) in (float, int) and type(expected) in (float, int) and abs(actual - expected) < 1e-9)


def validate_cases(cases: list[dict[str, Any]]) -> None:
    for case in cases:
        if not case.get('message') or not case.get('category'):
            raise ValueError('Each routing case needs a message and category')
        expected = case['expected']
        if expected['action'] not in {'route', 'clarify', 'reject', 'direct_reply'}:
            raise ValueError('Invalid expected routing action')
        if (expected.get('workflow') in {'Research', 'Portfolio'}) != (expected['action'] == 'route'):
            raise ValueError('Only route actions select a workflow')


def evaluate(cases: list[dict[str, Any]], predictions: list[dict[str, Any]]) -> dict[str, Any]:
    validate_cases(cases)
    by_id = {p['id']: p for p in predictions}
    if len(by_id) != len(predictions) or len({c['id'] for c in cases}) != len(cases):
        raise ValueError('Duplicate IDs')
    if set(by_id) - {c['id'] for c in cases}:
        raise ValueError('Predictions contain unknown case IDs')
    rows = []
    action_matrix: dict[str, Counter] = {}
    workflow_matrix: dict[str, Counter] = {}
    for case in cases:
        expected = case['expected']
        prediction = by_id.get(case['id'])
        observed = prediction is not None
        prediction = prediction or {}
        action_ok = observed and prediction.get('action') == expected['action']
        workflow_ok = observed and prediction.get('workflow') == expected.get('workflow')
        params_ok = observed and contains(prediction.get('params', {}), expected.get('params', {}))
        rows.append(dict(id=case['id'], category=case['category'], observed=observed,
                         action_correct=action_ok, workflow_correct=workflow_ok,
                         parameters_correct=params_ok, passed=action_ok and workflow_ok and params_ok))
        if observed:
            action_matrix.setdefault(expected['action'], Counter())[str(prediction.get('action'))] += 1
            workflow_matrix.setdefault(str(expected.get('workflow')), Counter())[str(prediction.get('workflow'))] += 1
    n = len(rows)
    observed_count = sum(row['observed'] for row in rows)
    def accuracy(field: str) -> float | None:
        return sum(row[field] for row in rows) / n if n and observed_count else None
    return dict(status='not_run' if not observed_count else 'complete' if observed_count == n else 'partial',
                case_count=n, observed_count=observed_count, missing_count=n-observed_count,
                accuracy=accuracy('passed'), action_accuracy=accuracy('action_correct'),
                workflow_accuracy=accuracy('workflow_correct'), parameter_accuracy=accuracy('parameters_correct'),
                action_confusion_matrix={key: dict(value) for key, value in action_matrix.items()},
                workflow_confusion_matrix={key: dict(value) for key, value in workflow_matrix.items()},
                results=rows)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cases', required=True)
    parser.add_argument('--predictions', help='Captured decisions; omitted produces a not-run manifest')
    parser.add_argument('--out', required=True)
    args = parser.parse_args(argv)
    summary = evaluate(read_jsonl(args.cases), read_jsonl(args.predictions) if args.predictions else [])
    output = Path(args.out)
    output.mkdir(parents=True, exist_ok=True)
    (output/'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2)+'\n')
    rate = 'not measured' if summary['accuracy'] is None else f"{summary['accuracy']:.1%}"
    (output/'report.md').write_text(f"# Router evaluation\n\nStatus: {summary['status']}. Observed {summary['observed_count']}/{summary['case_count']}. Accuracy: {rate}.\n\nMissing predictions count as failures only when a partial run has observations. No observations means unknown accuracy, not zero or 100%.\n\nConfusion matrices and per-case results: summary.json. No LLM judge is used.\n")
    print(f"{summary['status']}: {summary['observed_count']}/{summary['case_count']} observed; accuracy {rate}")
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
