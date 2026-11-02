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
from .scorers import JUDGE_SCORES, case_passed, detected_behavior, score_case


def _git_commit() -> str | None:
    try:
        return subprocess.run(["git", "rev-parse", "--short", "HEAD"], capture_output=True, text=True,
                              check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def _select(cases: list[EvalCase], include_unverified: bool, categories: list[str] | None,
            ids: list[str] | None) -> list[EvalCase]:
    selected = [c for c in cases if include_unverified or c.verified]
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
        "judge_error": next((item.details["error"] for item in items
                             if item.name in JUDGE_SCORES and item.details.get("error")), None),
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


def _now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")


RUN_META_FILE = "run_meta.json"


def _run_meta(args: argparse.Namespace) -> dict:
    """Facts about how the answers were PRODUCED. Written once by `run`, never rewritten by `rescore`."""
    return {
        "label": args.label,
        "mode": args.mode,
        "base_url": args.base_url,
        # the system under test; defaults to this checkout's HEAD, override when the API runs another build
        "system_commit": args.system_commit or _git_commit(),
        "started_at": _now(),
        "notes": args.notes,
    }


def _score_meta(args: argparse.Namespace, run_meta: dict, judge: LlmJudge | None) -> dict:
    """Run provenance plus facts about how the answers were SCORED (which may happen later)."""
    return {
        **run_meta,
        "label": args.label,
        "cases_file": str(args.cases),
        "cases_sha256": hashlib.sha256(Path(args.cases).read_bytes()).hexdigest(),
        "scorer_commit": _git_commit(),
        "scored_at": _now(),
        "judge_model": judge.config.model if judge else None,
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
    mark = {True: "PASS", False: "FAIL", None: "DRAFT"}[record["passed"]]
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

    all_cases = load_cases(args.cases)
    cases = _select(all_cases, args.include_unverified, args.category, args.case)
    if not cases:
        print("no cases selected (unverified cases need --include-unverified)", file=sys.stderr)
        return 2
    skipped_drafts = sum(1 for c in all_cases if not c.verified) if not args.include_unverified else 0
    if skipped_drafts:
        print(f"skipping {skipped_drafts} unverified case(s); pass --include-unverified to run them as drafts",
              file=sys.stderr)
    judge = _make_judge(args.no_judge)
    out_dir = Path(args.out or Path(__file__).resolve().parent.parent / "runs" / args.label)
    out_dir.mkdir(parents=True, exist_ok=True)
    token = args.token or os.getenv("EQUITYLENS_TOKEN")
    run_meta = _run_meta(args)
    (out_dir / RUN_META_FILE).write_text(json.dumps(run_meta, ensure_ascii=False, indent=2), encoding="utf-8")
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
    summary = _write_outputs(out_dir, records, _score_meta(args, run_meta, judge))
    _print_result(summary, out_dir)
    return 0


def _print_result(summary: dict, out_dir: Path) -> None:
    pass_rate = summary["metrics"].get("pass_rate", {}).get("mean")
    print(f"\npass_rate={pass_rate} over {summary['verified_case_count']} verified case(s)"
          f" → {out_dir / 'report.md'}")


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
    source_dir = Path(args.responses).resolve().parent
    out_dir = Path(args.out).resolve() if args.out else source_dir.parent / args.label
    if out_dir == source_dir:
        print("rescore must write to a new directory so the original run's report stays intact; "
              "choose a different --label or --out", file=sys.stderr)
        return 2
    run_meta_path = source_dir / RUN_META_FILE
    if run_meta_path.exists():
        run_meta = json.loads(run_meta_path.read_text(encoding="utf-8"))
    else:
        run_meta = {"mode": outputs[0].mode if outputs else "unknown", "system_commit": None}
        print(f"warning: {run_meta_path} not found; system provenance unknown", file=sys.stderr)
    meta = {**_score_meta(args, run_meta, judge), "rescored_from": str(source_dir)}
    summary = _write_outputs(out_dir, records, meta)
    _print_result(summary, out_dir)
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
    run.add_argument("--include-unverified", action="store_true",
                     help="also run draft cases; they are scored but never counted in pass_rate")
    run.add_argument("--system-commit", help="commit of the API under test (default: this checkout's HEAD)")
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
