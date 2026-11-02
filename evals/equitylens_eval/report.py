"""Aggregate per-case scores into a summary and compare two runs."""

from __future__ import annotations

import json
import math
from collections import defaultdict
from pathlib import Path
from statistics import mean
from typing import Any

METRIC_ORDER = (
    "pass_rate",
    "behavior",
    "facts",
    "keywords",
    "citation_integrity",
    "citation_coverage",
    "source_hit",
    "retrieval_recall",
    "judge_correctness",
    "judge_groundedness",
)
LOWER_IS_BETTER = {"hallucination_rate", "over_refusal_rate", "error_rate", "latency_p50_ms",
                   "latency_p95_ms", "mean_total_tokens", "total_cost_usd", "judge_error_count"}


def _percentile(values: list[float], pct: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    rank = max(0, min(len(ordered) - 1, math.ceil(pct * len(ordered)) - 1))
    return ordered[rank]


def _metric_means(records: list[dict[str, Any]]) -> dict[str, dict[str, float | int]]:
    buckets: dict[str, list[float]] = defaultdict(list)
    for record in records:
        if record["passed"] is not None:  # None = draft case, never part of pass_rate
            buckets["pass_rate"].append(1.0 if record["passed"] else 0.0)
        for name, score in record["scores"].items():
            if score is not None:
                buckets[name].append(score)
    return {name: {"mean": round(mean(values), 4), "n": len(values)} for name, values in buckets.items()}


def summarize(records: list[dict[str, Any]], meta: dict[str, Any]) -> dict[str, Any]:
    """Quality metrics use verified cases only; drafts are summarized separately for diagnostics.

    Latency, tokens, cost and error rate cover every case that was sent, because they were all paid for.
    """
    verified = [r for r in records if r["passed"] is not None]
    drafts = [r for r in records if r["passed"] is None]
    by_category: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for record in verified:
        by_category[record["category"]].append(record)

    should_decline = [r for r in verified if r["expected_behavior"] == "insufficient_evidence"]
    should_answer = [r for r in verified if r["expected_behavior"] == "answer"]
    latencies = [r["latency_ms"] for r in records if r.get("latency_ms") is not None]
    tokens = [r["total_tokens"] for r in records if r.get("total_tokens") is not None]
    costs = [r["cost_usd"] for r in records if r.get("cost_usd") is not None]

    operational = {
        "judge_error_count": sum(bool(r.get("judge_error")) for r in records),
        # answered although the corpus has no answer: the metric that matters most in finance
        "hallucination_rate": round(sum(r["detected_behavior"] == "answer" for r in should_decline)
                                    / len(should_decline), 4) if should_decline else None,
        "over_refusal_rate": round(sum(r["detected_behavior"] == "insufficient_evidence" for r in should_answer)
                                   / len(should_answer), 4) if should_answer else None,
        "error_rate": round(sum(r["detected_behavior"] == "error" for r in records) / len(records), 4)
        if records else None,
        "latency_p50_ms": _percentile(latencies, 0.50),
        "latency_p95_ms": _percentile(latencies, 0.95),
        "mean_total_tokens": round(mean(tokens), 1) if tokens else None,
        "total_cost_usd": round(sum(costs), 6) if costs else None,
    }
    return {
        "meta": meta,
        "case_count": len(records),
        "verified_case_count": len(verified),
        "draft_case_count": len(drafts),
        "metrics": _metric_means(verified),
        "operational": operational,
        "by_category": {cat: {"case_count": len(rs), "metrics": _metric_means(rs)}
                        for cat, rs in sorted(by_category.items())},
        "failed_cases": [r["case_id"] for r in verified if r["passed"] is False],
        "draft_cases": [r["case_id"] for r in drafts],
        "draft_metrics": _metric_means(drafts) if drafts else {},
    }


def _fmt(value: Any) -> str:
    if value is None:
        return "—"
    if isinstance(value, float):
        return f"{value:.3f}".rstrip("0").rstrip(".") if abs(value) < 1000 else f"{value:,.0f}"
    return str(value)


def render_markdown(summary: dict[str, Any], records: list[dict[str, Any]]) -> str:
    meta = summary["meta"]
    lines = [
        f"# Eval report — {meta.get('label')}",
        "",
        f"- mode: `{meta.get('mode')}` · verified cases: {summary['verified_case_count']}"
        f" · drafts (not in pass_rate): {summary['draft_case_count']}",
        f"- system commit: `{meta.get('system_commit')}` · scorer commit: `{meta.get('scorer_commit')}`"
        + (f" · rescored from `{meta['rescored_from']}`" if meta.get("rescored_from") else ""),
        f"- cases file: `{meta.get('cases_file')}` (sha256 {str(meta.get('cases_sha256'))[:12]})",
        f"- judge: {meta.get('judge_model') or 'disabled'} · started: {meta.get('started_at')}",
        "",
        "## Quality",
        "",
        "| metric | mean | n |",
        "|---|---|---|",
    ]
    for name in METRIC_ORDER:
        metric = summary["metrics"].get(name)
        if metric:
            lines.append(f"| {name} | {_fmt(metric['mean'])} | {metric['n']} |")
    lines += ["", "## Behaviour, cost and latency", "", "| metric | value |", "|---|---|"]
    for name, value in summary["operational"].items():
        lines.append(f"| {name} | {_fmt(value)} |")
    lines += ["", "## By category", "", "| category | cases | pass_rate | facts | source_hit |", "|---|---|---|---|---|"]
    for category, block in summary["by_category"].items():
        metrics = block["metrics"]
        lines.append(
            f"| {category} | {block['case_count']} | {_fmt(metrics.get('pass_rate', {}).get('mean'))} | "
            f"{_fmt(metrics.get('facts', {}).get('mean'))} | {_fmt(metrics.get('source_hit', {}).get('mean'))} |"
        )
    failed = [r for r in records if r["passed"] is False]
    if failed:
        lines += ["", "## Failed cases", ""]
        for record in failed:
            reasons = ", ".join(f"{k}={_fmt(v)}" for k, v in record["scores"].items() if v is not None and v < 1)
            if record.get("judge_error"):
                reasons = "; ".join(filter(None, (reasons, f"judge error: {record['judge_error']}")))
            lines.append(f"- **{record['case_id']}** ({record['category']}, detected `{record['detected_behavior']}`)"
                         f": {reasons or record.get('error') or 'see results.jsonl'}")
    drafts = [r for r in records if r["passed"] is None]
    if drafts:
        lines += ["", "## Draft cases (not verified, excluded from pass_rate)", ""]
        lines += [f"- {r['case_id']} (detected `{r['detected_behavior']}`)"
                  + (f": judge error: {r['judge_error']}" if r.get("judge_error") else "") for r in drafts]
    return "\n".join(lines) + "\n"


def load_run(run_dir: str | Path) -> tuple[dict[str, Any], dict[str, dict[str, Any]]]:
    run_dir = Path(run_dir)
    summary = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
    records = {}
    for line in (run_dir / "results.jsonl").read_text(encoding="utf-8").splitlines():
        if line.strip():
            record = json.loads(line)
            records[record["case_id"]] = record
    return summary, records


def compare(baseline_dir: str | Path, candidate_dir: str | Path) -> str:
    base_summary, base_records = load_run(baseline_dir)
    cand_summary, cand_records = load_run(candidate_dir)
    lines = [
        f"# Compare: {base_summary['meta'].get('label')} → {cand_summary['meta'].get('label')}",
        "",
        "| metric | baseline | candidate | Δ |",
        "|---|---|---|---|",
    ]
    if base_summary["meta"].get("cases_sha256") != cand_summary["meta"].get("cases_sha256"):
        lines[1:1] = ["", "> ⚠️ The two runs used different case files; metric deltas are not directly comparable.", ""]

    def row(name: str, base: float | None, cand: float | None) -> None:
        if base is None and cand is None:
            return
        delta = "—"
        if base is not None and cand is not None:
            diff = cand - base
            better = diff < 0 if name in LOWER_IS_BETTER else diff > 0
            mark = "" if abs(diff) < 1e-9 else (" ✅" if better else " ⚠️")
            delta = f"{diff:+.3f}{mark}" if abs(diff) < 1000 else f"{diff:+,.0f}{mark}"
        lines.append(f"| {name} | {_fmt(base)} | {_fmt(cand)} | {delta} |")

    for name in METRIC_ORDER:
        row(name, base_summary["metrics"].get(name, {}).get("mean"), cand_summary["metrics"].get(name, {}).get("mean"))
    for name in dict.fromkeys([*base_summary["operational"], *cand_summary["operational"]]):
        default = 0 if name == "judge_error_count" else None
        row(name, base_summary["operational"].get(name, default), cand_summary["operational"].get(name, default))

    shared = sorted(set(base_records) & set(cand_records))
    regressions = [c for c in shared if base_records[c]["passed"] is True and cand_records[c]["passed"] is False]
    fixes = [c for c in shared if base_records[c]["passed"] is False and cand_records[c]["passed"] is True]
    lines += ["", f"**Regressions ({len(regressions)})**: " + (", ".join(regressions) or "none"),
              "", f"**Fixed ({len(fixes)})**: " + (", ".join(fixes) or "none")]
    only = sorted(set(base_records) ^ set(cand_records))
    if only:
        lines += ["", f"_Cases present in only one run (not compared): {', '.join(only)}_"]
    return "\n".join(lines) + "\n"
