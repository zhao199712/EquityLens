"""Deterministic text helpers: number extraction, unit scaling, refusal detection.

Financial answers in Chinese mix formats freely ("59.1%", "新台幣2.89兆元",
"28,943 億元", "EPS 39.25 元"). Everything is reduced to a base value plus a
kind (percent / money / plain) so an expected fact can be compared numerically.
"""

from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass

MULTIPLIERS = {"兆": 1e12, "億": 1e8, "萬": 1e4, "千": 1e3}
MONEY_SUFFIXES = ("元", "美元", "新台幣", "NT$", "US$")

# number, optional multiplier, optional currency / percent marker
_NUMBER = re.compile(
    r"(?P<num>[-+]?\d{1,3}(?:,\d{3})+(?:\.\d+)?|[-+]?\d+(?:\.\d+)?)"
    r"\s*(?P<mult>兆|億|萬|千)?"
    r"\s*(?P<tail>%|％|個百分點|百分點|美元|元|倍)?"
)

REFUSAL_PATTERNS = (
    "資料不足",
    "不足以回答",
    "無法回答",
    "無法從提供的資料",
    "找不到相關資料",
    "沒有足夠",
    "insufficient evidence",
    "not enough information",
)

_CITATION = re.compile(r"\[(\d+)\]")
_SENTENCE_SPLIT = re.compile(r"(?<=[。！？!?；;\n])")


@dataclass(frozen=True)
class NumberMention:
    value: float  # scaled to base units (percent stays in percent points)
    kind: str  # "percent" | "money" | "multiple" | "plain"
    raw: str


def normalize_text(text: str) -> str:
    """NFKC folds full-width digits/percent signs into ASCII."""
    return unicodedata.normalize("NFKC", text or "")


def extract_numbers(text: str) -> list[NumberMention]:
    text = normalize_text(text).replace("−", "-")
    mentions: list[NumberMention] = []
    for match in _NUMBER.finditer(text):
        raw_num = match.group("num")
        # skip citation markers like [3]
        start = match.start("num")
        if start > 0 and text[start - 1] == "[" and text[match.end("num") : match.end("num") + 1] == "]":
            continue
        value = float(raw_num.replace(",", ""))
        mult = match.group("mult")
        tail = match.group("tail")
        if tail in ("%", "％", "個百分點", "百分點"):
            kind = "percent"
        elif tail == "倍":
            kind = "multiple"
        elif tail in ("元", "美元") or mult:
            kind = "money"
        else:
            kind = "plain"
        if mult and kind != "percent":
            value *= MULTIPLIERS[mult]
        mentions.append(NumberMention(value=value, kind=kind, raw=match.group(0).strip()))
    return mentions


def fact_kind_and_scale(unit: str) -> tuple[str, float]:
    """Map an expected fact's unit to (kind, multiplier to base units)."""
    unit = normalize_text(unit).strip()
    if unit in ("%", "百分點", "個百分點"):
        return "percent", 1.0
    if unit == "倍":
        return "multiple", 1.0
    if unit == "":
        return "plain", 1.0
    scale = 1.0
    for symbol, multiplier in MULTIPLIERS.items():
        if unit.startswith(symbol):
            scale = multiplier
            break
    return "money", scale


def default_tolerance(kind: str) -> tuple[float, str]:
    if kind == "percent":
        return 0.1, "abs"  # 0.1 percentage points
    if kind == "money":
        return 0.005, "rel"  # 0.5 %: absorbs rounding like 2.89兆 vs 28,943億
    return 0.01, "abs"


def within_tolerance(actual: float, expected: float, tolerance: float, tolerance_type: str) -> bool:
    if tolerance_type == "rel":
        if expected == 0:
            return abs(actual) <= tolerance
        return abs(actual - expected) <= abs(expected) * tolerance
    return abs(actual - expected) <= tolerance


def is_refusal(answer: str) -> bool:
    lowered = normalize_text(answer).lower()
    return any(pattern.lower() in lowered for pattern in REFUSAL_PATTERNS)


def citation_indices(answer: str) -> list[int]:
    return [int(x) for x in _CITATION.findall(answer or "")]


def split_sentences(text: str) -> list[str]:
    return [s.strip() for s in _SENTENCE_SPLIT.split(text or "") if s.strip()]


def contains_any(text: str, aliases: tuple[str, ...]) -> bool:
    haystack = normalize_text(text).lower()
    return any(normalize_text(alias).lower() in haystack for alias in aliases)
