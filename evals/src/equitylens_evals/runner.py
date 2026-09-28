"""Run a dataset against the system and score every case."""

from __future__ import annotations

import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

from . import __version__
from .client import ResearchSystem
from .judge import OpenAICompatibleJudge
from .schema import CaseResult, EvalCase, RunMetadata, RunReport, file_sha256, load_cases
from .scorers import score_case


def select_cases(
    cases: Iterable[EvalCase],
    *,
    include_drafts: bool = False,
    categories: set[str] | None = None,
    ids: set[str] | None = None,
) -> list[EvalCase]:
    selected = []
    for case in cases:
        if case.status == "draft" and not include_drafts:
            continue
        if categories and case.category not in categories:
            continue
        if ids and case.id not in ids:
            continue
        selected.append(case)
    return selected


def git_commit(start: Path) -> str | None:
    try:
        return subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"], cwd=start, capture_output=True, text=True, check=True
        ).stdout.strip() or None
    except (OSError, subprocess.CalledProcessError):
        return None


def _now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def run_eval(
    dataset: Path,
    system: ResearchSystem,
    *,
    mode: str,
    api_base_url: str,
    include_drafts: bool = False,
    categories: set[str] | None = None,
    ids: set[str] | None = None,
    concurrency: int = 1,
    judge: OpenAICompatibleJudge | None = None,
    label: str | None = None,
    log=lambda message: print(message, file=sys.stderr),
) -> RunReport:
    cases = select_cases(load_cases(dataset), include_drafts=include_drafts, categories=categories, ids=ids)
    if not cases:
        raise ValueError("no cases selected; reviewed cases only unless --include-drafts is set")

    metadata = RunMetadata(
        harness_version=__version__,
        mode=mode,  # type: ignore[arg-type]
        dataset_path=str(dataset),
        dataset_sha256=file_sha256(dataset),
        git_commit=git_commit(dataset.parent),
        api_base_url=api_base_url,
        started_at_utc=_now(),
        label=label,
        judge_model=judge.model if judge else None,
        judge_prompt_version=None,
        included_drafts=include_drafts,
    )

    def evaluate(case: EvalCase) -> CaseResult:
        output = system.run(case.request)
        result = score_case(case, output)
        if judge is not None:
            try:
                result.judge = judge.judge(case, output)
            except Exception as exc:  # noqa: BLE001 - a judge failure must not lose the run
                log(f"  judge failed for {case.id}: {exc}")
        mark = "PASS" if result.passed else "FAIL"
        failed = [c.name for c in result.checks if not c.passed and not c.skipped]
        log(f"  {mark} {case.id}" + (f"  ({', '.join(failed)})" if failed else ""))
        return result

    log(f"Running {len(cases)} cases in {mode} mode against {api_base_url}")
    with ThreadPoolExecutor(max_workers=max(1, concurrency)) as pool:
        results = list(pool.map(evaluate, cases))

    if judge is not None:
        from .judge import PROMPT_VERSION

        metadata.judge_prompt_version = PROMPT_VERSION
    metadata.finished_at_utc = _now()
    return RunReport(metadata=metadata, results=results)
