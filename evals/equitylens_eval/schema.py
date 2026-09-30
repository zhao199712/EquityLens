"""Data shapes for eval cases and normalized system outputs.

Cases are authored by hand in JSONL (one case per line). Outputs are normalized
from either `/api/research/ask` or the `/api/research/investigations` agent
workflow so that every scorer sees the same shape regardless of mode.
"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any

CATEGORIES = {
    "numeric_lookup",  # one number from one document
    "multi_doc_synthesis",  # needs several chunks / documents
    "qualitative",  # risks, strategy, management commentary
    "unanswerable",  # answer is NOT in the corpus; system must decline
    "policy",  # source-policy / guardrail behaviour (e.g. LocalOnly + fresh news)
}
BEHAVIORS = {"answer", "insufficient_evidence"}
MODES = {"ask", "investigation"}


@dataclass(frozen=True)
class ExpectedFact:
    label: str
    value: float | None  # None = not yet verified against the source document
    unit: str = ""  # "%", "元", "億元", "兆元", "倍", "" ...
    tolerance: float | None = None
    tolerance_type: str | None = None  # "abs" (in the fact's unit) or "rel"

    @property
    def verified(self) -> bool:
        return self.value is not None


@dataclass(frozen=True)
class ExpectedSource:
    document_type: str | None = None  # AnnualReport / EarningsPresentation / Web
    page: int | None = None
    page_tolerance: int = 1
    title_contains: str | None = None


@dataclass(frozen=True)
class EvalCase:
    id: str
    category: str
    ticker: str
    question: str
    expected_behavior: str = "answer"
    request: dict[str, Any] = field(default_factory=dict)
    reference_answer: str | None = None
    facts: tuple[ExpectedFact, ...] = ()
    must_include: tuple[tuple[str, ...], ...] = ()  # each group: any alias matches
    must_not_include: tuple[str, ...] = ()
    sources: tuple[ExpectedSource, ...] = ()
    verified: bool = False
    tags: tuple[str, ...] = ()
    notes: str = ""

    @staticmethod
    def from_dict(raw: dict[str, Any]) -> "EvalCase":
        return EvalCase(
            id=raw["id"],
            category=raw["category"],
            ticker=str(raw["ticker"]),
            question=raw["question"],
            expected_behavior=raw.get("expected_behavior", "answer"),
            request=dict(raw.get("request") or {}),
            reference_answer=raw.get("reference_answer"),
            facts=tuple(ExpectedFact(**f) for f in raw.get("facts") or []),
            must_include=tuple(
                tuple([g] if isinstance(g, str) else g) for g in raw.get("must_include") or []
            ),
            must_not_include=tuple(raw.get("must_not_include") or []),
            sources=tuple(ExpectedSource(**s) for s in raw.get("sources") or []),
            verified=bool(raw.get("verified", False)),
            tags=tuple(raw.get("tags") or []),
            notes=raw.get("notes", ""),
        )


def validate_case(case: EvalCase) -> list[str]:
    """Return human-readable problems; empty list means the case is well formed."""
    problems: list[str] = []
    if case.category not in CATEGORIES:
        problems.append(f"unknown category '{case.category}'")
    if case.expected_behavior not in BEHAVIORS:
        problems.append(f"unknown expected_behavior '{case.expected_behavior}'")
    if case.category == "unanswerable" and case.expected_behavior != "insufficient_evidence":
        problems.append("unanswerable cases must expect insufficient_evidence")
    if case.expected_behavior == "answer" and not (
        case.facts or case.must_include or case.reference_answer
    ):
        problems.append("answer cases need facts, must_include or a reference_answer to be scorable")
    if case.verified and case.expected_behavior == "answer" and not (
        any(f.verified for f in case.facts) or case.must_include
    ):
        problems.append("verified answer cases need verified facts or must_include; "
                        "a reference_answer alone is only checked when the LLM judge runs")
    if case.verified and any(not f.verified for f in case.facts):
        problems.append("case is marked verified but has facts with value=null")
    for source in case.sources:
        if not (source.document_type or source.page is not None or source.title_contains):
            problems.append("an expected source must set document_type, page or title_contains")
    for fact in case.facts:
        if fact.tolerance_type not in (None, "abs", "rel"):
            problems.append(f"fact '{fact.label}': tolerance_type must be abs or rel")
    return problems


def load_cases(path: str | Path) -> list[EvalCase]:
    cases: list[EvalCase] = []
    seen: set[str] = set()
    for line_no, line in enumerate(Path(path).read_text(encoding="utf-8").splitlines(), start=1):
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        try:
            case = EvalCase.from_dict(json.loads(stripped))
        except (json.JSONDecodeError, KeyError, TypeError) as exc:
            raise ValueError(f"{path}:{line_no}: invalid case: {exc}") from exc
        if case.id in seen:
            raise ValueError(f"{path}:{line_no}: duplicate case id '{case.id}'")
        seen.add(case.id)
        cases.append(case)
    return cases


@dataclass
class Citation:
    index: int
    source_type: str | None = None
    document_type: str | None = None
    page: int | None = None
    title: str | None = None
    quote: str | None = None
    url: str | None = None


@dataclass
class RetrievedCandidate:
    document_type: str | None
    page: int | None
    title: str | None
    rank: int | None
    selected: bool


@dataclass
class RunOutput:
    """What the system produced for one case, normalized across modes."""

    case_id: str
    mode: str
    status: str  # Answered / InsufficientEvidence / Failed / Error
    answer: str = ""
    citations: list[Citation] = field(default_factory=list)
    candidates: list[RetrievedCandidate] = field(default_factory=list)
    prompt_tokens: int | None = None
    completion_tokens: int | None = None
    cost_usd: float | None = None
    latency_ms: int | None = None
    model: str | None = None
    run_id: str | None = None
    error: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @staticmethod
    def from_dict(raw: dict[str, Any]) -> "RunOutput":
        data = dict(raw)
        data["citations"] = [Citation(**c) for c in data.get("citations") or []]
        data["candidates"] = [RetrievedCandidate(**c) for c in data.get("candidates") or []]
        return RunOutput(**data)
