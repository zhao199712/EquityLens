"""Command line entry point.

  python -m equitylens_eval validate --cases cases/research_v1.jsonl
  python -m equitylens_eval run      --cases cases/research_v1.jsonl --mode ask --label prompt-v4
  python -m equitylens_eval rescore  --responses runs/prompt-v4/responses.jsonl --cases ... --label prompt-v4-rescored
  python -m equitylens_eval compare  runs/prompt-v4 runs/prompt-v5

`run` stores raw system outputs in responses.jsonl, so scorers can be improved
and re-applied with `rescore` without paying for the API calls again.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Iterable

from .judge import JudgeConfig, LlmJudge
from .report import compare, render_markdown, summarize
from .schema import MODES, EvalCase, RunOutput, load_cases, validate_case
from .scorers import case_passed, detected_behavior, score_case


def _git_commit() -> str | None:
    try:
        return subprocess.run(["git", "rev-parse", "--short", "HEAD"], capture_output=True, text=True,
                              check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def _select(cases: list[EvalCase], only_verified: bool, categories: list[str] | None,
            ids: list[str] | None) -> list[EvalCase]:
    selected = [c for c in cases if (not only_verified or c.verified)]
    if categories:
        selected = [c for c in selected if c.category in categories]
    if ids:
        selected = [c for c in selected if c.id in ids]
    return selected


def build_record(case: EvalCase, output: RunOutput, judge: LlmJudge | None) -> dict:
    items = score_case(case, output)
    if judge is not None:
        items += judge.judge(case, output)
    total_tokens = None
    if output.prompt_tokens is not None or output.completion_tokens is not None:
        total_tokens = (output.prompt_tokens or 0) + (output.completion_tokens or 0)
    return {
        "case_id": case.id,
        "category": case.category,
        "verified": case.verified,
        "expected_behavior": case.expected_behavior,
        "detected_behavior": detected_behavior(output),
        "passed": case_passed(case, items),
        "scores": {item.name: item.score for item in items},
        "details": {item.name: item.details for item in items if item.details},
        "status": output.status,
        "latency_ms": output.latency_ms,
        "total_tokens": total_tokens,
        "cost_usd": output.cost_usd,
        "run_id": output.run_id,
        "error": output.error,
        "answer_preview": output.answer[:200],
    }


def _write_outputs(out_dir: Path, records: list[dict], meta: dict) -> dict:
    out_dir.mkdir(parents=True, exist_ok=True)
    with (out_dir / "results.jsonl").open("w", encoding="utf-8") as handle:
        for record in records:
            handle.write(json.dumps(record, ensure_ascii=False) + "\n")
    summary = summarize(records, meta)
    (out_dir / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    (out_dir / "report.md").write_text(render_markdown(summary, records), encoding="utf-8")
    return summary


def _meta(args: argparse.Namespace, mode: str, judge: LlmJudge | None) -> dict:
    cases_bytes = Path(args.cases).read_bytes()
    return {
        "label": args.label,
        "mode": mode,
        "cases_file": str(args.cases),
        "cases_sha256": hashlib.sha256(cases_bytes).hexdigest(),
        "git_commit": _git_commit(),
        "judge_model": judge.config.model if judge else None,
        "started_at": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
        "notes": args.notes,
    }


def _make_judge(disabled: bool) -> LlmJudge | None:
    if disabled:
        return None
    config = JudgeConfig.from_env()
    if config is None:
        print("judge: EVAL_JUDGE_* not set, running deterministic scorers only", file=sys.stderr)
        return None
    return LlmJudge(config)


def _progress(index: int, total: int, record: dict) -> None:
    mark = "PASS" if record["passed"] else "FAIL"
    print(f"[{index}/{total}] {mark} {record['case_id']} ({record['detected_behavior']}, "
          f"{record['latency_ms']} ms)", file=sys.stderr)


def cmd_validate(args: argparse.Namespace) -> int:
    cases = load_cases(args.cases)
    bad = 0
    for case in cases:
        problems = validate_case(case)
        if problems:
            bad += 1
            print(f"{case.id}: " + "; ".join(problems))
    unverified = [c.id for c in cases if not c.verified]
    print(f"{len(cases)} cases, {bad} with problems, {len(unverified)} not yet verified")
    if unverified and args.verbose:
        print("unverified: " + ", ".join(unverified))
    return 1 if bad else 0


def cmd_run(args: argparse.Namespace) -> int:
    from .client import EquityLensClient  # imported lazily so rescore/compare work without network deps

    cases = _select(load_cases(args.cases), args.only_verified, args.category, args.case)
    if not cases:
        print("no cases selected", file=sys.stderr)
        return 2
    judge = _make_judge(args.no_judge)
    out_dir = Path(args.out or Path(__file__).resolve().parent.parent / "runs" / args.label)
    out_dir.mkdir(parents=True, exist_ok=True)
    token = args.token or os.getenv("EQUITYLENS_TOKEN")
    records = []
    with EquityLensClient(args.base_url, token=token, investigation_timeout_s=args.investigation_timeout) as client, \
            (out_dir / "responses.jsonl").open("w", encoding="utf-8") as responses:
        for index, case in enumerate(cases, start=1):
            output = client.run(case, args.mode)
            responses.write(json.dumps(output.to_dict(), ensure_ascii=False) + "\n")
            responses.flush()
            record = build_record(case, output, judge)
            records.append(record)
            _progress(index, len(cases), record)
    summary = _write_outputs(out_dir, records, _meta(args, args.mode, judge))
    print(f"\npass_rate={summary['metrics']['pass_rate']['mean']} → {out_dir / 'report.md'}")
    return 0


def _read_responses(path: str | Path) -> Iterable[RunOutput]:
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        if line.strip():
            yield RunOutput.from_dict(json.loads(line))


def cmd_rescore(args: argparse.Namespace) -> int:
    cases = {c.id: c for c in load_cases(args.cases)}
    judge = _make_judge(args.no_judge)
    outputs = list(_read_responses(args.responses))
    records = [build_record(cases[o.case_id], o, judge) for o in outputs if o.case_id in cases]
    skipped = [o.case_id for o in outputs if o.case_id not in cases]
    if skipped:
        print(f"skipped responses with unknown case ids: {', '.join(skipped)}", file=sys.stderr)
    mode = outputs[0].mode if outputs else "unknown"
    out_dir = Path(args.out or Path(args.responses).parent)
    summary = _write_outputs(out_dir, records, _meta(args, mode, judge))
    print(f"pass_rate={summary['metrics']['pass_rate']['mean']} → {out_dir / 'report.md'}")
    return 0


def cmd_compare(args: argparse.Namespace) -> int:
    print(compare(args.baseline, args.candidate))
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="equitylens_eval", description="EquityLens research eval harness")
    sub = parser.add_subparsers(dest="command", required=True)

    validate = sub.add_parser("validate", help="lint a cases file")
    validate.add_argument("--cases", required=True)
    validate.add_argument("-v", "--verbose", action="store_true")
    validate.set_defaults(func=cmd_validate)

    run = sub.add_parser("run", help="run cases against a live API")
    run.add_argument("--cases", required=True)
    run.add_argument("--mode", choices=sorted(MODES), default="ask")
    run.add_argument("--label", required=True, help="name for this run, e.g. prompt-v4 or jev-assessor")
    run.add_argument("--base-url", default=os.getenv("EQUITYLENS_BASE_URL", "http://localhost:5035"))
    run.add_argument("--token", help="JWT bearer token (default: $EQUITYLENS_TOKEN)")
    run.add_argument("--out")
    run.add_argument("--only-verified", action="store_true")
    run.add_argument("--category", action="append")
    run.add_argument("--case", action="append", help="run only this case id (repeatable)")
    run.add_argument("--no-judge", action="store_true")
    run.add_argument("--investigation-timeout", type=float, default=900.0)
    run.add_argument("--notes", default="")
    run.set_defaults(func=cmd_run)

    rescore = sub.add_parser("rescore", help="re-score saved responses without calling the API")
    rescore.add_argument("--responses", required=True)
    rescore.add_argument("--cases", required=True)
    rescore.add_argument("--label", required=True)
    rescore.add_argument("--out")
    rescore.add_argument("--no-judge", action="store_true")
    rescore.add_argument("--notes", default="")
    rescore.set_defaults(func=cmd_rescore)

    comp = sub.add_parser("compare", help="diff two run directories")
    comp.add_argument("baseline")
    comp.add_argument("candidate")
    comp.set_defaults(func=cmd_compare)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
