"""Deterministic scorers.

Everything here is plain code: no LLM, no network, same input -> same score.
The optional LLM judge (judge.py) is layered on top for things code cannot
decide, such as whether a paraphrase is faithful to its citation.

Gating checks decide pass/fail. Informational checks are reported as
metrics but never fail a case, because a correct answer drawn from a
different valid page should not count as wrong.
"""

from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass
from typing import Callable

from .numbers import extract_numbers, within_tolerance
from .schema import SCALE_FACTORS, CaseResult, CheckResult, EvalCase, ExpectedSource, SystemOutput

INSUFFICIENT_STATUS = "InsufficientEvidence"
# Kept in sync with ResearchAnswerService.InsufficientEvidencePatterns, plus the
# phrasings the agent workflow's reviser tends to use.
ABSTAIN_PHRASES = (
    "資料不足",
    "無法回答",
    "資料中沒有",
    "文件中沒有",
    "文件均未提及",
    "未提供相關",
    "not enough information",
    "cannot answer",
    "do not have enough information",
)
_CITATION_MARKER = re.compile(r"\[(\d+)\]")
_SENTENCE_SPLIT = re.compile(r"(?<=[。！？；\n])|(?<=[.!?])\s+")
_BARE_YEAR = re.compile(r"^(19|20)\d{2}$")


@dataclass(frozen=True)
class Check:
    name: str
    gating: bool
    fn: Callable[[EvalCase, SystemOutput], CheckResult]


def _normalize(text: str) -> str:
    return re.sub(r"\s+", "", unicodedata.normalize("NFKC", text)).lower()


def abstained(output: SystemOutput) -> bool:
    if output.status == INSUFFICIENT_STATUS:
        return True
    answer = _normalize(output.answer)
    return any(_normalize(p) in answer for p in ABSTAIN_PHRASES)


def check_abstention(case: EvalCase, output: SystemOutput) -> CheckResult:
    did_abstain = abstained(output)
    if case.expected.answerable:
        return CheckResult(
            name="abstention",
            passed=not did_abstain,
            score=0.0 if did_abstain else 1.0,
            detail="over-refusal: answerable question was declined" if did_abstain else "answered",
        )
    return CheckResult(
        name="abstention",
        passed=did_abstain,
        score=1.0 if did_abstain else 0.0,
        detail="correctly declined" if did_abstain else "hallucination risk: answered an unanswerable question",
    )


def check_numeric_facts(case: EvalCase, output: SystemOutput) -> CheckResult:
    facts = case.expected.numeric_facts
    if not facts or not case.expected.answerable:
        return CheckResult(name="numeric_facts", passed=True, score=1.0, skipped=True)
    mentions = extract_numbers(output.answer)
    missed: list[str] = []
    for fact in facts:
        want_percent = fact.unit == "%"
        target = fact.value if want_percent else fact.absolute_value
        abs_tol = fact.abs_tolerance
        if abs_tol is not None and not want_percent:
            abs_tol *= SCALE_FACTORS[fact.scale]  # tolerance is written in the same scale as `value`
        hit = any(
            m.is_percent == want_percent and within_tolerance(m.value, target, abs_tol, fact.rel_tolerance)
            for m in mentions
        )
        if not hit:
            missed.append(f"{fact.label}={fact.value}{fact.unit if want_percent else ''}")
    score = (len(facts) - len(missed)) / len(facts)
    return CheckResult(
        name="numeric_facts",
        passed=not missed,
        score=score,
        detail="all numbers matched" if not missed else "missing: " + ", ".join(missed),
    )


def check_required_points(case: EvalCase, output: SystemOutput) -> CheckResult:
    groups = case.expected.required_points
    if not groups or not case.expected.answerable:
        return CheckResult(name="required_points", passed=True, score=1.0, skipped=True)
    answer = _normalize(output.answer)
    missed = [g[0] for g in groups if not any(_normalize(s) in answer for s in g)]
    score = (len(groups) - len(missed)) / len(groups)
    return CheckResult(
        name="required_points",
        passed=not missed,
        score=score,
        detail="all points covered" if not missed else "missing: " + ", ".join(missed),
    )


def check_forbidden_points(case: EvalCase, output: SystemOutput) -> CheckResult:
    forbidden = case.expected.forbidden_points
    if not forbidden:
        return CheckResult(name="forbidden_points", passed=True, score=1.0, skipped=True)
    answer = _normalize(output.answer)
    hits = [f for f in forbidden if _normalize(f) in answer]
    return CheckResult(
        name="forbidden_points",
        passed=not hits,
        score=0.0 if hits else 1.0,
        detail="clean" if not hits else "contains: " + ", ".join(hits),
    )


def check_citation_integrity(case: EvalCase, output: SystemOutput) -> CheckResult:
    """Every [n] must point at a returned citation, and a substantive answer
    must cite something. Mirrors CitationValidator in the API, so a failure
    here means the API-side guard was bypassed or regressed."""
    if abstained(output):
        return CheckResult(name="citation_integrity", passed=True, score=1.0, skipped=True)
    valid = {c.index for c in output.citations}
    markers = [int(m) for m in _CITATION_MARKER.findall(output.answer)]
    dangling = sorted({m for m in markers if m not in valid})
    if not markers:
        return CheckResult(name="citation_integrity", passed=False, score=0.0, detail="answer has no citations")
    if dangling:
        return CheckResult(
            name="citation_integrity",
            passed=False,
            score=1.0 - len(dangling) / len(set(markers)),
            detail=f"dangling citation markers: {dangling}",
        )
    return CheckResult(name="citation_integrity", passed=True, score=1.0, detail=f"{len(set(markers))} citations valid")


def _has_substantive_number(sentence: str) -> bool:
    """True when a sentence states a figure. Bare years ("2025 年") and
    single-digit list numbering do not count as claims."""
    for mention in extract_numbers(sentence):
        if mention.is_percent:
            return True
        bare = mention.text.replace(",", "")
        if _BARE_YEAR.match(bare) or (bare.isdigit() and len(bare) == 1):
            continue
        return True
    return False


def check_numeric_claim_coverage(case: EvalCase, output: SystemOutput) -> CheckResult:
    """Share of sentences that state a number and also carry a citation.
    Uncited numbers are the most expensive kind of hallucination in finance."""
    if abstained(output):
        return CheckResult(name="numeric_claim_coverage", passed=True, score=1.0, skipped=True)
    stripped = _CITATION_MARKER.sub(lambda m: f"⟦{m.group(1)}⟧", output.answer)
    sentences = [s for s in _SENTENCE_SPLIT.split(stripped) if s.strip()]
    numeric = [s for s in sentences if _has_substantive_number(re.sub(r"⟦\d+⟧", "", s))]
    if not numeric:
        return CheckResult(name="numeric_claim_coverage", passed=True, score=1.0, skipped=True)
    cited = sum(1 for s in numeric if "⟦" in s)
    score = cited / len(numeric)
    return CheckResult(
        name="numeric_claim_coverage",
        passed=score == 1.0,
        score=score,
        detail=f"{cited}/{len(numeric)} numeric sentences cited",
    )


def _source_matches(expected: ExpectedSource, output: SystemOutput) -> bool:
    for c in output.citations:
        if expected.document_type and c.document_type != expected.document_type:
            continue
        if expected.title_contains and (not c.title or _normalize(expected.title_contains) not in _normalize(c.title)):
            continue
        if expected.pages:
            if c.page is None or not any(abs(c.page - p) <= expected.page_slack for p in expected.pages):
                continue
        return True
    return False


def check_source_recall(case: EvalCase, output: SystemOutput) -> CheckResult:
    sources = case.expected.sources
    if not sources or not case.expected.answerable:
        return CheckResult(name="source_recall", passed=True, score=1.0, skipped=True)
    hits = sum(1 for s in sources if _source_matches(s, output))
    return CheckResult(
        name="source_recall",
        passed=hits == len(sources),
        score=hits / len(sources),
        detail=f"{hits}/{len(sources)} expected sources cited",
    )


CHECKS: tuple[Check, ...] = (
    Check("abstention", True, check_abstention),
    Check("numeric_facts", True, check_numeric_facts),
    Check("required_points", True, check_required_points),
    Check("forbidden_points", True, check_forbidden_points),
    Check("citation_integrity", True, check_citation_integrity),
    Check("numeric_claim_coverage", False, check_numeric_claim_coverage),
    Check("source_recall", False, check_source_recall),
)
GATING = {c.name for c in CHECKS if c.gating}


def score_case(case: EvalCase, output: SystemOutput) -> CaseResult:
    if output.error:
        checks = [CheckResult(name="request", passed=False, score=0.0, detail=output.error)]
        return CaseResult(
            case_id=case.id, category=case.category, difficulty=case.difficulty,
            passed=False, checks=checks, output=output,
        )
    checks = [c.fn(case, output) for c in CHECKS]
    passed = all(r.passed for r in checks if r.name in GATING and not r.skipped)
    return CaseResult(
        case_id=case.id, category=case.category, difficulty=case.difficulty,
        passed=passed, checks=checks, output=output,
    )
