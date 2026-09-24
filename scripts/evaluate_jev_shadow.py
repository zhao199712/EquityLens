#!/usr/bin/env python3
"""Evaluate manually labeled public evidence pairs from the Jev shadow trace.

The input is JSONL with one question/passage pair per line. It intentionally
contains no passage text or credentials. See docs/research/jev-evidence-shadow.md.
"""

import argparse
import json
from collections import defaultdict
from pathlib import Path
from urllib.parse import urlparse


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("labels", type=Path)
    parser.add_argument("--allowed-host", action="append", required=True)
    parser.add_argument("--usd-per-million-input", type=float, default=0.042)
    return parser.parse_args()


def boolean(row, name):
    value = row.get(name)
    if not isinstance(value, bool):
        raise ValueError(f"{name} must be a manually assigned JSON boolean")
    return value


def probability(row, name):
    value = row.get(name)
    if not isinstance(value, (int, float)) or isinstance(value, bool) or not 0 <= value <= 1:
        raise ValueError(f"{name} must be a probability in [0, 1]")
    return float(value)


def confusion(rows, gold, score):
    counts = defaultdict(int)
    for row in rows:
        actual = boolean(row, gold)
        predicted = probability(row, score) >= 0.5
        counts[(actual, predicted)] += 1
    tp, fn = counts[(True, True)], counts[(True, False)]
    fp, tn = counts[(False, True)], counts[(False, False)]
    return {
        "tp": tp, "fn": fn, "fp": fp, "tn": tn,
        "recall": round(tp / (tp + fn), 3) if tp + fn else None,
        "precision": round(tp / (tp + fp), 3) if tp + fp else None,
    }


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, round((len(ordered) - 1) * fraction))]


def main():
    args = parse_args()
    allowed_hosts = {host.lower() for host in args.allowed_host}
    rows = []
    seen = set()
    for line_number, line in enumerate(args.labels.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        row = json.loads(line)
        pair = (row["question_id"], row["chunk_id"])
        if pair in seen:
            raise ValueError(f"line {line_number}: duplicate question/chunk pair")
        seen.add(pair)
        url = urlparse(row["source_url"])
        if url.scheme != "https" or (url.hostname or "").lower() not in allowed_hosts:
            raise ValueError(f"line {line_number}: source is not an allowlisted public HTTPS host")
        if row.get("language") not in ("zh", "en"):
            raise ValueError(f"line {line_number}: language must be zh or en")
        for key in ("gold_relevant", "gold_direct", "gold_contradiction", "gold_web_needed", "old_web"):
            boolean(row, key)
        for key in ("jev_relevance", "jev_direct", "jev_contradiction"):
            probability(row, key)
        if row.get("jev_would_web") is not None:
            boolean(row, "jev_would_web")
        rows.append(row)

    if len(rows) < 100:
        raise ValueError(f"need at least 100 manually labeled public pairs; found {len(rows)}")
    if {row["language"] for row in rows} != {"zh", "en"}:
        raise ValueError("benchmark must include both Chinese and English pairs")

    questions = {}
    for row in rows:
        key = row["question_id"]
        decision = (row["gold_web_needed"], row["old_web"], row.get("jev_would_web"))
        if key in questions and questions[key] != decision:
            raise ValueError(f"inconsistent Web decisions for question {key}")
        questions[key] = decision

    comparable = [item for item in questions.values() if item[2] is not None]
    durations_by_question = {}
    for row in rows:
        if isinstance(row.get("duration_ms"), (int, float)):
            key = row["question_id"]
            if key in durations_by_question and durations_by_question[key] != row["duration_ms"]:
                raise ValueError(f"inconsistent Jev duration for question {key}")
            durations_by_question[key] = row["duration_ms"]
    durations = list(durations_by_question.values())
    tokens = sum(row.get("input_tokens", 0) for row in rows)
    report = {
        "pairs": len(rows),
        "questions": len(questions),
        "language_counts": {lang: sum(row["language"] == lang for row in rows) for lang in ("zh", "en")},
        "relevance": confusion(rows, "gold_relevant", "jev_relevance"),
        "direct_answer": confusion(rows, "gold_direct", "jev_direct"),
        "contradiction": confusion(rows, "gold_contradiction", "jev_contradiction"),
        "web_decisions": {
            "comparable_questions": len(comparable),
            "old_missed_gaps": sum(gold and not old for gold, old, _ in comparable),
            "jev_missed_gaps": sum(gold and not jev for gold, _, jev in comparable),
            "old_unnecessary_searches": sum(not gold and old for gold, old, _ in comparable),
            "jev_unnecessary_searches": sum(not gold and jev for gold, _, jev in comparable),
        },
        "input_tokens": tokens,
        "estimated_jev_usd": round(tokens * args.usd_per_million_input / 1_000_000, 6),
        "jev_batch_duration_ms_p50": percentile(durations, 0.5),
        "jev_batch_duration_ms_p95": percentile(durations, 0.95),
    }
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
