"""Extract numbers from Chinese / English financial prose.

Answers say things like "毛利率為 56.1%", "營收 2.89 兆元" or "淨利 8,946 億元".
The extractor turns each mention into an absolute value so a fact written
as 8,946 (hundred_million) matches "8,946 億", "0.8946 兆" or
"894.6 billion".
"""

from __future__ import annotations

import re
from dataclasses import dataclass

_MULTIPLIERS: dict[str, float] = {
    "兆": 1e12,
    "億": 1e8,
    "萬": 1e4,
    "千": 1e3,
    "trillion": 1e12,
    "billion": 1e9,
    "million": 1e6,
    "thousand": 1e3,
}
_PERCENT_SUFFIXES = {"%", "％", "個百分點", "百分點"}

_NUMBER = re.compile(
    r"(?P<sign>[-−–])?\s*"
    r"(?P<num>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)"
    r"\s*(?P<suffix>%|％|個百分點|百分點|兆|億|萬|千|trillion|billion|million|thousand)?",
    re.IGNORECASE,
)


@dataclass(frozen=True)
class NumberMention:
    value: float          # absolute value (percent stays in percent units, e.g. 56.1)
    is_percent: bool
    text: str
    start: int
    end: int


def extract_numbers(text: str) -> list[NumberMention]:
    mentions: list[NumberMention] = []
    for match in _NUMBER.finditer(text):
        raw = match.group("num").replace(",", "")
        try:
            value = float(raw)
        except ValueError:
            continue
        if match.group("sign"):
            value = -value
        suffix = (match.group("suffix") or "").lower()
        is_percent = suffix in _PERCENT_SUFFIXES
        if suffix in _MULTIPLIERS:
            value *= _MULTIPLIERS[suffix]
        mentions.append(NumberMention(value, is_percent, match.group(0).strip(), match.start(), match.end()))
    return mentions


def within_tolerance(actual: float, expected: float, abs_tol: float | None, rel_tol: float | None) -> bool:
    if abs_tol is not None and abs(actual - expected) <= abs_tol:
        return True
    if rel_tol is not None and expected != 0 and abs(actual - expected) / abs(expected) <= rel_tol:
        return True
    return actual == expected
