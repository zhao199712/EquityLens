"""Aggregate a run into metrics, and diff two runs."""

from __future__ import annotations

import math
from collections import defaultdict
from statistics import mean

from .schema import CaseResult, RunReport
from .scorers import CHECKS, abstained


def _pct(values: list[bool]) -> float | None:
    return sum(values) / len(values) if values else None


def _percentile(values: list[int], q: float) -> int | None:
    if not values:
        return None
    ordered = sorted(values)
    return ordered[max(0, math.ceil(q * len(ordered)) - 1)]


def summarize(report: RunReport) -> dict:
    results = report.results
    scored = [r for r in results if not r.output.error]
    summary: dict = {
        "cases": len(results),
        "errors": len(results) - len(scored),
        "pass_rate": _pct([r.passed for r in results]),
        "by_category": {},
        "by_difficulty": {},
        "checks": {},
    }

    for key, attr in (("by_category", "category"), ("by_difficulty", "difficulty")):
        groups: dict[str, list[CaseResult]] = defaultdict(list)
        for r in results:
            groups[getattr(r, attr)].append(r)
        summary[key] = {name: {"cases": len(rs), "pass_rate": _pct([r.passed for r in rs])} for name, rs in sorted(groups.items())}

    for check in CHECKS:
        applied = [c for r in scored for c in r.checks if c.name == check.name and not c.skipped]
        summary["checks"][check.name] = {
            "gating": check.gating,
            "applied": len(applied),
            "pass_rate": _pct([c.passed for c in applied]),
            "mean_score": mean(c.score for c in applied) if applied else None,
        }

    unanswerable = [r for r in scored if r.category == "unanswerable"]
    answerable = [r for r in scored if r.category != "unanswerable"]
    summary["refusal"] = {
        # Of questions with no answer in the corpus, how many did we decline?
        "correct_refusal_rate": _pct([abstained(r.output) for r in unanswerable]),
        # Of questions that do have an answer, how many did we wrongly decline?
        "over_refusal_rate": _pct([abstained(r.output) for r in answerable]),
    }

    latencies = [r.output.latency_ms for r in scored if r.output.latency_ms is not None]
    tokens = [(r.output.input_tokens or 0) + (r.output.output_tokens or 0) for r in scored
              if r.output.input_tokens is not None or r.output.output_tokens is not None]
    costs = [r.output.cost_usd for r in scored if r.output.cost_usd is not None]
    summary["latency_ms"] = {"p50": _percentile(latencies, 0.5), "p95": _percentile(latencies, 0.95)}
    summary["tokens"] = {"total": sum(tokens) if tokens else None, "mean": mean(tokens) if tokens else None}
    summary["cost_usd"] = {"total": sum(costs) if costs else None, "mean": mean(costs) if costs else None}

    judged = [r.judge for r in results if r.judge]
    summary["judge"] = {
        "judged": len(judged),
        "faithfulness_mean": mean(j.faithfulness for j in judged) if judged else None,
        "completeness_mean": mean(j.completeness for j in judged) if judged else None,
    }
    return summary


def _f(value, kind: str = "pct") -> str:
    if value is None:
        return "—"
    if kind == "pct":
        return f"{value * 100:.1f}%"
    if kind == "ms":
        return f"{value / 1000:.1f}s"
    if kind == "usd":
        return f"${value:.4f}"
    if kind == "num":
        return f"{value:,.0f}"
    return f"{value:.2f}"


def render_markdown(report: RunReport) -> str:
    s = summarize(report)
    m = report.metadata
    lines = [
        f"# Eval report{f' — {m.label}' if m.label else ''}",
        "",
        f"- Mode: `{m.mode}` · commit `{m.git_commit or 'unknown'}` · dataset `{m.dataset_sha256[:12]}`"
        + (" · **includes drafts**" if m.included_drafts else ""),
        f"- Started {m.started_at_utc}, finished {m.finished_at_utc}",
    ]
    if m.judge_model:
        lines.append(f"- Judge: `{m.judge_model}` (prompt v{m.judge_prompt_version})")
    lines += [
        "",
        "## Headline",
        "",
        "| Metric | Value |",
        "|---|---|",
        f"| Pass rate | **{_f(s['pass_rate'])}** ({s['cases']} cases, {s['errors']} errors) |",
        f"| Correct refusal (unanswerable) | {_f(s['refusal']['correct_refusal_rate'])} |",
        f"| Over-refusal (answerable) | {_f(s['refusal']['over_refusal_rate'])} |",
        f"| Latency p50 / p95 | {_f(s['latency_ms']['p50'], 'ms')} / {_f(s['latency_ms']['p95'], 'ms')} |",
        f"| Tokens total / mean | {_f(s['tokens']['total'], 'num')} / {_f(s['tokens']['mean'], 'num')} |",
        f"| Cost total / mean | {_f(s['cost_usd']['total'], 'usd')} / {_f(s['cost_usd']['mean'], 'usd')} |",
    ]
    if s["judge"]["judged"]:
        lines.append(
            f"| Judge faithfulness / completeness (1–5) | {_f(s['judge']['faithfulness_mean'], 'x')} / {_f(s['judge']['completeness_mean'], 'x')} |"
        )
    lines += ["", "## Checks", "", "| Check | Gating | Applied | Pass rate | Mean score |", "|---|---|---|---|---|"]
    for name, c in s["checks"].items():
        lines.append(f"| {name} | {'yes' if c['gating'] else 'info'} | {c['applied']} | {_f(c['pass_rate'])} | {_f(c['mean_score'], 'x')} |")
    lines += ["", "## By category", "", "| Category | Cases | Pass rate |", "|---|---|---|"]
    for name, c in s["by_category"].items():
        lines.append(f"| {name} | {c['cases']} | {_f(c['pass_rate'])} |")
    failures = [r for r in report.results if not r.passed]
    if failures:
        lines += ["", "## Failures", ""]
        for r in failures:
            reasons = "; ".join(f"{c.name}: {c.detail}" for c in r.checks if not c.passed and not c.skipped)
            lines.append(f"- `{r.case_id}` — {reasons}")
    return "\n".join(lines) + "\n"


_COMPARE_ROWS = (
    ("Pass rate", lambda s: s["pass_rate"], "pct", True),
    ("Correct refusal", lambda s: s["refusal"]["correct_refusal_rate"], "pct", True),
    ("Over-refusal", lambda s: s["refusal"]["over_refusal_rate"], "pct", False),
    ("Numeric facts (mean)", lambda s: s["checks"]["numeric_facts"]["mean_score"], "pct", True),
    ("Required points (mean)", lambda s: s["checks"]["required_points"]["mean_score"], "pct", True),
    ("Citation integrity", lambda s: s["checks"]["citation_integrity"]["pass_rate"], "pct", True),
    ("Numeric claim coverage", lambda s: s["checks"]["numeric_claim_coverage"]["mean_score"], "pct", True),
    ("Source recall", lambda s: s["checks"]["source_recall"]["mean_score"], "pct", True),
    ("Judge faithfulness", lambda s: s["judge"]["faithfulness_mean"], "x", True),
    ("Latency p50", lambda s: s["latency_ms"]["p50"], "ms", False),
    ("Latency p95", lambda s: s["latency_ms"]["p95"], "ms", False),
    ("Mean tokens", lambda s: s["tokens"]["mean"], "num", False),
    ("Mean cost", lambda s: s["cost_usd"]["mean"], "usd", False),
)


def render_comparison(baseline: RunReport, candidate: RunReport) -> str:
    a, b = summarize(baseline), summarize(candidate)
    name_a = baseline.metadata.label or f"{baseline.metadata.mode}@{baseline.metadata.git_commit}"
    name_b = candidate.metadata.label or f"{candidate.metadata.mode}@{candidate.metadata.git_commit}"
    lines = [f"# Eval comparison: {name_a} → {name_b}", ""]
    if baseline.metadata.dataset_sha256 != candidate.metadata.dataset_sha256:
        lines += ["> ⚠️ The two runs used different datasets. Deltas below are not apples-to-apples.", ""]
    lines += ["| Metric | Baseline | Candidate | Change |", "|---|---|---|---|"]
    for label, get, kind, higher_is_better in _COMPARE_ROWS:
        va, vb = get(a), get(b)
        if va is None and vb is None:
            continue
        change = "—"
        if va is not None and vb is not None:
            delta = vb - va
            if kind == "pct":
                text = f"{delta * 100:+.1f} pp"
            elif kind == "ms":
                text = f"{delta / 1000:+.1f}s"
            elif kind == "usd":
                text = f"{delta:+.4f}"
            elif kind == "num":
                text = f"{delta:+,.0f}"
            else:
                text = f"{delta:+.2f}"
            # Changes that round to zero at display precision are noise, not a signal.
            negligible = not any(ch in "123456789" for ch in text)
            better = delta > 0 if higher_is_better else delta < 0
            change = text if negligible else f"{text} {'✅' if better else '🔻'}"
        lines.append(f"| {label} | {_f(va, kind)} | {_f(vb, kind)} | {change} |")

    before = {r.case_id: r for r in baseline.results}
    after = {r.case_id: r for r in candidate.results}
    shared = sorted(before.keys() & after.keys())
    regressions = [i for i in shared if before[i].passed and not after[i].passed]
    fixes = [i for i in shared if not before[i].passed and after[i].passed]
    lines += ["", f"## Regressions ({len(regressions)})", ""]
    for case_id in regressions:
        reasons = "; ".join(f"{c.name}: {c.detail}" for c in after[case_id].checks if not c.passed and not c.skipped)
        lines.append(f"- `{case_id}` — {reasons}")
    if not regressions:
        lines.append("None.")
    lines += ["", f"## Fixed ({len(fixes)})", ""]
    lines += [f"- `{case_id}`" for case_id in fixes] or ["None."]
    only = sorted(before.keys() ^ after.keys())
    if only:
        lines += ["", f"Cases present in only one run: {', '.join(only)}"]
    return "\n".join(lines) + "\n"
