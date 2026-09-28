"""Command line entry point: `python -m equitylens_evals <command>`."""

from __future__ import annotations

import argparse
import json
import os
import sys
from collections import Counter
from pathlib import Path

from .client import AskMode, EquityLensClient, InvestigationMode
from .judge import OpenAICompatibleJudge
from .report import render_comparison, render_markdown, summarize
from .runner import run_eval, select_cases
from .schema import RunReport, load_cases


def _client(args) -> EquityLensClient:
    token = os.environ.get("EQUITYLENS_TOKEN")
    email = os.environ.get("EQUITYLENS_EMAIL")
    password = os.environ.get("EQUITYLENS_PASSWORD")
    if not token and not (email and password):
        sys.exit("Set EQUITYLENS_TOKEN, or EQUITYLENS_EMAIL and EQUITYLENS_PASSWORD.")
    return EquityLensClient(args.base_url, token=token, email=email, password=password)


def _judge() -> OpenAICompatibleJudge:
    base_url = os.environ.get("EVAL_JUDGE_BASE_URL", "https://api.openai.com/v1")
    api_key = os.environ.get("EVAL_JUDGE_API_KEY")
    model = os.environ.get("EVAL_JUDGE_MODEL")
    if not api_key or not model:
        sys.exit("--judge needs EVAL_JUDGE_API_KEY and EVAL_JUDGE_MODEL (EVAL_JUDGE_BASE_URL is optional).")
    return OpenAICompatibleJudge(base_url, api_key, model)


def cmd_validate(args) -> None:
    cases = load_cases(Path(args.dataset))
    status = Counter(c.status for c in cases)
    category = Counter(c.category for c in cases if c.status == "reviewed")
    print(f"OK: {len(cases)} cases ({status['reviewed']} reviewed, {status['draft']} draft)")
    for name, count in sorted(category.items()):
        print(f"  {name:<14} {count}")
    if status["reviewed"] < 20:
        print(f"note: {status['reviewed']} reviewed cases; aim for 20-30 before trusting the pass rate.")


def cmd_run(args) -> None:
    client = _client(args)
    system = AskMode(client) if args.mode == "ask" else InvestigationMode(client, max_wait_seconds=args.max_wait)
    report = run_eval(
        Path(args.dataset),
        system,
        mode=args.mode,
        api_base_url=args.base_url,
        include_drafts=args.include_drafts,
        categories=set(args.category) if args.category else None,
        ids=set(args.id) if args.id else None,
        concurrency=args.concurrency,
        judge=_judge() if args.judge else None,
        label=args.label,
    )
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(report.model_dump_json(indent=2), encoding="utf-8")
    markdown = render_markdown(report)
    out.with_suffix(".md").write_text(markdown, encoding="utf-8")
    print(markdown)
    print(f"Saved {out} and {out.with_suffix('.md')}", file=sys.stderr)
    if args.fail_under is not None:
        rate = summarize(report)["pass_rate"] or 0.0
        if rate < args.fail_under:
            sys.exit(f"pass rate {rate:.1%} is below --fail-under {args.fail_under:.1%}")


def _load_report(path: str) -> RunReport:
    return RunReport.model_validate_json(Path(path).read_text(encoding="utf-8"))


def cmd_report(args) -> None:
    report = _load_report(args.result)
    print(json.dumps(summarize(report), ensure_ascii=False, indent=2) if args.json else render_markdown(report))


def cmd_compare(args) -> None:
    text = render_comparison(_load_report(args.baseline), _load_report(args.candidate))
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
    print(text)


def cmd_worksheet(args) -> None:
    """Run draft cases and print what the system answered, so a reviewer can
    check the filing and fill in `expected`. Verify against the PDF itself:
    copying the system's answer into the gold set would grade it against itself."""
    client = _client(args)
    system = AskMode(client)
    drafts = [c for c in select_cases(load_cases(Path(args.dataset)), include_drafts=True) if c.status == "draft"]
    lines = ["# Annotation worksheet", "", "Check each answer against the source PDF before writing `expected`.", ""]
    for case in drafts:
        output = system.run(case.request)
        lines += [f"## `{case.id}` ({case.category})", "", f"**Q:** {case.request.ticker} — {case.request.question}", ""]
        lines += [f"**System ({output.status}):** {output.error or output.answer}", ""]
        for c in output.citations:
            lines.append(f"- [{c.index}] {c.title or '?'} p.{c.page} — {(c.quote or '')[:160]}")
        lines.append("")
    Path(args.out).write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {args.out} ({len(drafts)} draft cases)")


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(prog="equitylens-evals", description="Evaluate EquityLens research answers.")
    sub = parser.add_subparsers(dest="command", required=True)
    default_dataset = str(Path(__file__).resolve().parents[2] / "datasets" / "research-v1.jsonl")
    base_url = os.environ.get("EQUITYLENS_BASE_URL", "http://localhost:5035")

    p = sub.add_parser("validate", help="Check dataset schema and coverage")
    p.add_argument("--dataset", default=default_dataset)
    p.set_defaults(func=cmd_validate)

    p = sub.add_parser("run", help="Run the dataset against a live API")
    p.add_argument("--dataset", default=default_dataset)
    p.add_argument("--mode", choices=["ask", "investigation"], default="ask")
    p.add_argument("--base-url", default=base_url)
    p.add_argument("--out", required=True, help="Result JSON path; a .md summary is written next to it")
    p.add_argument("--label", help="Name shown in reports, e.g. 'prompt-v5'")
    p.add_argument("--include-drafts", action="store_true")
    p.add_argument("--category", action="append")
    p.add_argument("--id", action="append")
    p.add_argument("--concurrency", type=int, default=1)
    p.add_argument("--max-wait", type=float, default=600.0, help="Investigation mode: seconds per case")
    p.add_argument("--judge", action="store_true", help="Add LLM-as-judge scores")
    p.add_argument("--fail-under", type=float, help="Exit non-zero if pass rate is below this (0-1), for CI")
    p.set_defaults(func=cmd_run)

    p = sub.add_parser("report", help="Re-render a saved result")
    p.add_argument("result")
    p.add_argument("--json", action="store_true")
    p.set_defaults(func=cmd_report)

    p = sub.add_parser("compare", help="Diff two saved results")
    p.add_argument("baseline")
    p.add_argument("candidate")
    p.add_argument("--out")
    p.set_defaults(func=cmd_compare)

    p = sub.add_parser("worksheet", help="Run draft cases to help write their expected answers")
    p.add_argument("--dataset", default=default_dataset)
    p.add_argument("--base-url", default=base_url)
    p.add_argument("--out", default="worksheet.md")
    p.set_defaults(func=cmd_worksheet)

    args = parser.parse_args(argv)
    args.func(args)


if __name__ == "__main__":
    main()
