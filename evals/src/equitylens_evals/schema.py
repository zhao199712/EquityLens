"""Data contracts for the EquityLens evaluation harness.

An eval case is one line of JSONL. It describes the request sent to the
research API and what a correct response must contain. Scoring never looks
at how the system got there, only at what it returned, so the same dataset
can compare single-shot RAG (`ask`) against the agent workflow
(`investigation`).
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

CaseStatus = Literal["draft", "reviewed"]
Category = Literal[
    "fact_lookup",        # 單一數字或事實，答案在一份文件的一頁內
    "multi_doc",          # 需要綜合兩份以上文件或兩個期間
    "qualitative",        # 風險、策略、展望等敘述型問題
    "temporal",           # 期間、年度、季別容易搞混的問題
    "unanswerable",       # 資料庫裡沒有答案，正確行為是說資料不足
]
Scale = Literal["one", "thousand", "ten_thousand", "hundred_million", "trillion"]

SCALE_FACTORS: dict[str, float] = {
    "one": 1.0,
    "thousand": 1e3,
    "ten_thousand": 1e4,
    "hundred_million": 1e8,
    "trillion": 1e12,
}


class _Strict(BaseModel):
    model_config = ConfigDict(extra="forbid", populate_by_name=True)


class ResearchRequest(_Strict):
    """Mirrors `ResearchAskRequest` in the API (camelCase on the wire)."""

    ticker: str
    question: str
    source_policy: Literal["Auto", "LocalOnly", "LocalThenWeb", "LocalAndWeb", "WebOnly"] = Field(
        default="LocalOnly", alias="sourcePolicy"
    )
    retrieval_mode: Literal["Auto", "ConferenceOnly", "AnnualReportOnly", "AllDocuments"] | None = Field(
        default=None, alias="retrievalMode"
    )
    document_type: Literal["AnnualReport", "EarningsPresentation"] | None = Field(
        default=None, alias="documentType"
    )
    top_k: int = Field(default=8, alias="topK", ge=1, le=50)

    def to_api(self) -> dict:
        return self.model_dump(by_alias=True, exclude_none=True)


class NumericFact(_Strict):
    """A number the answer must state, e.g. 毛利率 56.1%.

    `value` is written in the unit people read in the filing: 56.1 with
    unit "%", or 8,946 with scale "hundred_million" for 8,946 億元.
    """

    label: str
    value: float
    unit: Literal["%", "currency", "count", "ratio"] = "currency"
    scale: Scale = "one"
    abs_tolerance: float | None = None
    rel_tolerance: float | None = 0.005

    @property
    def absolute_value(self) -> float:
        return self.value * SCALE_FACTORS[self.scale]


class ExpectedSource(_Strict):
    """Where a reviewer found the answer. A citation matches when every
    provided field matches; omitted fields are wildcards."""

    document_type: Literal["AnnualReport", "EarningsPresentation"] | None = None
    title_contains: str | None = None
    pages: list[int] | None = None
    page_slack: int = 1


class Expected(_Strict):
    answerable: bool = True
    numeric_facts: list[NumericFact] = Field(default_factory=list)
    # Each inner list is a set of synonyms; the answer must mention at least one.
    required_points: list[list[str]] = Field(default_factory=list)
    forbidden_points: list[str] = Field(default_factory=list)
    sources: list[ExpectedSource] = Field(default_factory=list)
    reference_answer: str | None = None

    @model_validator(mode="after")
    def _unanswerable_has_no_facts(self) -> "Expected":
        if not self.answerable and (self.numeric_facts or self.required_points or self.sources):
            raise ValueError("unanswerable cases must not define facts, points or sources")
        return self


class EvalCase(_Strict):
    id: str = Field(pattern=r"^[a-z0-9][a-z0-9\-]*$")
    status: CaseStatus = "draft"
    category: Category
    difficulty: Literal["easy", "medium", "hard"] = "medium"
    request: ResearchRequest
    expected: Expected
    notes: str | None = None

    @model_validator(mode="after")
    def _reviewed_cases_are_gradable(self) -> "EvalCase":
        if self.status != "reviewed":
            return self
        e = self.expected
        if e.answerable and not (e.numeric_facts or e.required_points or e.reference_answer):
            raise ValueError(
                f"reviewed case {self.id!r} is answerable but has nothing to grade against"
            )
        return self


class Citation(_Strict):
    index: int
    document_type: str | None = None
    title: str | None = None
    page: int | None = None
    quote: str | None = None


class SystemOutput(_Strict):
    """Normalized response, regardless of which API mode produced it."""

    status: str
    answer: str
    citations: list[Citation] = Field(default_factory=list)
    latency_ms: int | None = None
    input_tokens: int | None = None
    output_tokens: int | None = None
    cost_usd: float | None = None
    research_run_id: str | None = None
    agent_run_id: str | None = None
    error: str | None = None


class CheckResult(_Strict):
    name: str
    passed: bool
    score: float = Field(ge=0.0, le=1.0)
    detail: str = ""
    skipped: bool = False


class JudgeResult(_Strict):
    faithfulness: int = Field(ge=1, le=5)
    completeness: int = Field(ge=1, le=5)
    rationale: str
    model: str
    prompt_version: int


class CaseResult(_Strict):
    case_id: str
    category: str
    difficulty: str
    passed: bool
    checks: list[CheckResult]
    output: SystemOutput
    judge: JudgeResult | None = None


class RunMetadata(_Strict):
    harness_version: str
    mode: Literal["ask", "investigation"]
    dataset_path: str
    dataset_sha256: str
    git_commit: str | None
    api_base_url: str
    started_at_utc: str
    finished_at_utc: str | None = None
    label: str | None = None
    judge_model: str | None = None
    judge_prompt_version: int | None = None
    included_drafts: bool = False


class RunReport(_Strict):
    metadata: RunMetadata
    results: list[CaseResult]


def load_cases(path: Path) -> list[EvalCase]:
    cases: list[EvalCase] = []
    seen: set[str] = set()
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip() or line.lstrip().startswith("//"):
            continue
        try:
            case = EvalCase.model_validate(json.loads(line))
        except Exception as exc:  # noqa: BLE001 - surface the line number
            raise ValueError(f"{path}:{number}: {exc}") from exc
        if case.id in seen:
            raise ValueError(f"{path}:{number}: duplicate case id {case.id!r}")
        seen.add(case.id)
        cases.append(case)
    return cases


def file_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()
