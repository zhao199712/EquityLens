"""Deterministic scorers. Each returns a ScoreItem with score in [0, 1] or None (not applicable).

Order mirrors the failure chain of a RAG answer, so a bad case can be localized:
retrieval (was the right page found?) -> citation (was it cited?) -> answer
(is the number right?) -> behaviour (did it decline when it should?).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

from . import normalize as n
from .schema import EvalCase, ExpectedSource, RunOutput


@dataclass
class ScoreItem:
    name: str
    score: float | None  # None = not applicable to this case
    details: dict[str, Any] = field(default_factory=dict)

    @property
    def applicable(self) -> bool:
        return self.score is not None


def detected_behavior(output: RunOutput) -> str:
    if output.status in ("Failed", "Error"):
        return "error"
    if output.status == "InsufficientEvidence" or n.is_refusal(output.answer):
        return "insufficient_evidence"
    return "answer"


def score_behavior(case: EvalCase, output: RunOutput) -> ScoreItem:
    actual = detected_behavior(output)
    return ScoreItem(
        "behavior",
        1.0 if actual == case.expected_behavior else 0.0,
        {"expected": case.expected_behavior, "actual": actual, "status": output.status},
    )


def _fact_matches(mention: n.NumberMention, expected_base: float, kind: str, scale: float,
                  tolerance: float, tolerance_type: str) -> bool:
    if mention.kind == kind:
        candidates = [mention.value]
    elif kind == "money" and mention.kind == "plain":
        # "營收 2.89" when the fact is 2.89 兆元: read the bare number in the fact's unit
        candidates = [mention.value * scale, mention.value]
    else:
        return False
    for value in candidates:
        if n.within_tolerance(value, expected_base, tolerance, tolerance_type):
            return True
        # sign-insensitive: "下降 1.2 個百分點" vs expected -1.2
        if n.within_tolerance(abs(value), abs(expected_base), tolerance, tolerance_type):
            return True
    return False


def score_facts(case: EvalCase, output: RunOutput) -> ScoreItem:
    verified = [f for f in case.facts if f.verified]
    if case.expected_behavior != "answer" or not verified:
        return ScoreItem("facts", None, {"unverified": [f.label for f in case.facts if not f.verified]})
    mentions = n.extract_numbers(output.answer)
    results = []
    for fact in verified:
        kind, scale = n.fact_kind_and_scale(fact.unit)
        default_tol, default_type = n.default_tolerance(kind)
        tolerance = fact.tolerance if fact.tolerance is not None else default_tol
        tolerance_type = fact.tolerance_type or default_type
        if tolerance_type == "abs" and kind == "money":
            tolerance *= scale  # abs tolerance is written in the fact's own unit
        expected_base = fact.value * scale
        hit = next(
            (m for m in mentions if _fact_matches(m, expected_base, kind, scale, tolerance, tolerance_type)),
            None,
        )
        results.append({"label": fact.label, "expected": f"{fact.value}{fact.unit}",
                        "matched": hit.raw if hit else None})
    matched = sum(1 for r in results if r["matched"])
    return ScoreItem("facts", matched / len(results), {"facts": results})


def score_keywords(case: EvalCase, output: RunOutput) -> ScoreItem:
    if not case.must_include and not case.must_not_include:
        return ScoreItem("keywords", None)
    missing = [list(group) for group in case.must_include if not n.contains_any(output.answer, group)]
    forbidden = [term for term in case.must_not_include if n.contains_any(output.answer, (term,))]
    if forbidden:
        score = 0.0  # a forbidden claim (e.g. "保證獲利") zeroes the item
    elif case.must_include:
        score = 1 - len(missing) / len(case.must_include)
    else:
        score = 1.0
    return ScoreItem("keywords", score, {"missing": missing, "forbidden_present": forbidden})


def score_citation_integrity(case: EvalCase, output: RunOutput) -> ScoreItem:
    """Every [n] in the answer must point at a returned citation (mirrors CitationValidator)."""
    if detected_behavior(output) != "answer":
        return ScoreItem("citation_integrity", None)
    refs = n.citation_indices(output.answer)
    if not refs:
        return ScoreItem("citation_integrity", 0.0, {"reason": "answer has no citation markers"})
    valid_indices = {c.index for c in output.citations}
    dangling = sorted({r for r in refs if r not in valid_indices})
    valid = sum(1 for r in refs if r in valid_indices)
    return ScoreItem("citation_integrity", valid / len(refs), {"references": len(refs), "dangling": dangling})


def _is_claim_number(mention: n.NumberMention) -> bool:
    """Percentages, money, multiples and large figures are claims; years and small counts are not."""
    if mention.kind != "plain":
        return True
    is_year = mention.value.is_integer() and 1900 <= mention.value <= 2100
    return mention.value >= 100 and not is_year


def score_citation_coverage(case: EvalCase, output: RunOutput) -> ScoreItem:
    """Share of sentences stating a number that also carry a citation marker."""
    if detected_behavior(output) != "answer":
        return ScoreItem("citation_coverage", None)
    numeric = [s for s in n.split_sentences(output.answer) if any(_is_claim_number(m) for m in n.extract_numbers(s))]
    if not numeric:
        return ScoreItem("citation_coverage", None)
    cited = [s for s in numeric if n.citation_indices(s)]
    uncited = [s[:60] for s in numeric if not n.citation_indices(s)]
    return ScoreItem("citation_coverage", len(cited) / len(numeric), {"uncited_numeric_sentences": uncited})


def _source_matches(expected: ExpectedSource, document_type: str | None, page: int | None,
                    title: str | None) -> bool:
    if expected.document_type and (document_type or "").lower() != expected.document_type.lower():
        return False
    if expected.page is not None:
        if page is None or abs(page - expected.page) > expected.page_tolerance:
            return False
    if expected.title_contains and expected.title_contains.lower() not in (title or "").lower():
        return False
    return True


def score_source_hit(case: EvalCase, output: RunOutput) -> ScoreItem:
    """Did the answer CITE the page where the truth lives?"""
    if not case.sources or case.expected_behavior != "answer":
        return ScoreItem("source_hit", None)
    hits = [any(_source_matches(s, c.document_type, c.page, c.title) for c in output.citations)
            for s in case.sources]
    return ScoreItem("source_hit", sum(hits) / len(hits), {"hits": hits})


def score_retrieval_recall(case: EvalCase, output: RunOutput) -> ScoreItem:
    """Did retrieval SURFACE the right page at all (selected or not)?

    source_hit low + retrieval_recall high  -> ranking / generation problem.
    both low                                -> retrieval problem.
    """
    if not case.sources or case.expected_behavior != "answer" or not output.candidates:
        return ScoreItem("retrieval_recall", None)
    hits = [any(_source_matches(s, c.document_type, c.page, c.title) for c in output.candidates)
            for s in case.sources]
    return ScoreItem("retrieval_recall", sum(hits) / len(hits), {"hits": hits,
                                                                 "candidates": len(output.candidates)})


DETERMINISTIC_SCORERS = (
    score_behavior,
    score_facts,
    score_keywords,
    score_citation_integrity,
    score_citation_coverage,
    score_source_hit,
    score_retrieval_recall,
)


def score_case(case: EvalCase, output: RunOutput) -> list[ScoreItem]:
    return [scorer(case, output) for scorer in DETERMINISTIC_SCORERS]


def case_passed(case: EvalCase, items: list[ScoreItem]) -> bool:
    """Strict gate: right behaviour, every verified fact, no forbidden text, no dangling citation."""
    by_name = {item.name: item for item in items}
    if by_name["behavior"].score != 1.0:
        return False
    for name in ("facts", "citation_integrity"):
        item = by_name.get(name)
        if item and item.applicable and item.score < 1.0:
            return False
    keywords = by_name.get("keywords")
    if keywords and keywords.applicable and keywords.details.get("forbidden_present"):
        return False
    return True
