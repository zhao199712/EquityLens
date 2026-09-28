import pytest

from equitylens_eval import normalize as n
from equitylens_eval.schema import Citation, EvalCase, RetrievedCandidate, RunOutput, validate_case
from equitylens_eval.scorers import case_passed, score_case


def make_case(**overrides):
    raw = {"id": "c1", "category": "numeric_lookup", "ticker": "2330", "question": "q", "verified": True}
    raw.update(overrides)
    return EvalCase.from_dict(raw)


def make_output(answer, status="Answered", citations=None, candidates=None):
    return RunOutput(case_id="c1", mode="ask", status=status, answer=answer,
                     citations=citations or [], candidates=candidates or [])


def by_name(items):
    return {item.name: item for item in items}


# ---- number extraction -----------------------------------------------------------------

@pytest.mark.parametrize("text,value,kind", [
    ("毛利率為 59.1%", 59.1, "percent"),
    ("毛利率為５９．１％", 59.1, "percent"),  # full-width
    ("營收 2.89 兆元", 2.89e12, "money"),
    ("營收 28,943 億元", 2.8943e12, "money"),
    ("EPS 39.25 元", 39.25, "money"),
    ("本益比 22.5 倍", 22.5, "multiple"),
    ("年增 3.2 個百分點", 3.2, "percent"),
])
def test_extract_numbers(text, value, kind):
    mentions = n.extract_numbers(text)
    assert any(abs(m.value - value) < 1e-6 * max(1, value) and m.kind == kind for m in mentions), mentions


def test_citation_markers_are_not_numbers():
    assert n.extract_numbers("毛利率 59.1%[2]") == [n.NumberMention(59.1, "percent", "59.1%")]


def test_refusal_detection():
    assert n.is_refusal("目前提供的資料不足以回答此問題。")
    assert not n.is_refusal("毛利率為 59.1%[1]")


# ---- facts -------------------------------------------------------------------------------

def test_fact_matches_across_unit_scales():
    case = make_case(facts=[{"label": "營收", "value": 28943, "unit": "億元"}])
    items = by_name(score_case(case, make_output("2025 年營收約 2.89 兆元[1]", citations=[Citation(1)])))
    assert items["facts"].score == 1.0  # 2.89 兆 vs 28,943 億 within default 0.5 % tolerance


def test_fact_wrong_number_fails():
    case = make_case(facts=[{"label": "毛利率", "value": 59.1, "unit": "%"}])
    items = by_name(score_case(case, make_output("毛利率為 56.1%[1]", citations=[Citation(1)])))
    assert items["facts"].score == 0.0
    assert not case_passed(case, list(items.values()))


def test_percent_fact_does_not_match_plain_number():
    case = make_case(facts=[{"label": "毛利率", "value": 59.1, "unit": "%"}])
    items = by_name(score_case(case, make_output("第 59.1 頁提到[1]", citations=[Citation(1)])))
    assert items["facts"].score == 0.0


def test_unverified_facts_are_not_applicable():
    case = make_case(verified=False, facts=[{"label": "毛利率", "value": None, "unit": "%"}],
                     must_include=[["毛利率"]])
    items = by_name(score_case(case, make_output("毛利率為 59.1%[1]", citations=[Citation(1)])))
    assert items["facts"].score is None
    assert items["facts"].details["unverified"] == ["毛利率"]


# ---- behaviour ---------------------------------------------------------------------------

def test_hallucinated_answer_on_unanswerable_case_fails():
    case = make_case(category="unanswerable", expected_behavior="insufficient_evidence")
    items = by_name(score_case(case, make_output("2030 年營收預估為 5 兆元[1]", citations=[Citation(1)])))
    assert items["behavior"].score == 0.0
    assert not case_passed(case, list(items.values()))


def test_decline_on_unanswerable_case_passes():
    case = make_case(category="unanswerable", expected_behavior="insufficient_evidence")
    items = score_case(case, make_output("目前提供的資料不足以回答此問題。", status="InsufficientEvidence"))
    assert case_passed(case, items)


def test_forbidden_phrase_fails_even_when_other_keywords_present():
    case = make_case(category="policy", must_include=[["風險"]], must_not_include=["一定會賺"])
    items = by_name(score_case(case, make_output("有風險，但長期一定會賺[1]", citations=[Citation(1)])))
    assert items["keywords"].score == 0.0
    assert not case_passed(case, list(items.values()))


# ---- citations and sources ---------------------------------------------------------------

def test_dangling_citation_is_detected():
    case = make_case(must_include=[["毛利率"]])
    items = by_name(score_case(case, make_output("毛利率 59.1%[1][3]", citations=[Citation(1)])))
    assert items["citation_integrity"].score == 0.5
    assert items["citation_integrity"].details["dangling"] == [3]


def test_citation_coverage_counts_uncited_numeric_sentences():
    case = make_case(must_include=[["毛利率"]])
    answer = "毛利率 59.1%[1]。營業利益率 49.0%。"
    items = by_name(score_case(case, make_output(answer, citations=[Citation(1)])))
    assert items["citation_coverage"].score == 0.5


def test_source_hit_vs_retrieval_recall_localizes_failure():
    case = make_case(must_include=[["毛利率"]],
                     sources=[{"document_type": "AnnualReport", "page": 45, "page_tolerance": 1}])
    output = make_output(
        "毛利率 59.1%[1]",
        citations=[Citation(1, document_type="AnnualReport", page=12)],  # cited the wrong page
        candidates=[RetrievedCandidate("AnnualReport", 46, "2025 年報", 7, False)],  # right page was retrieved
    )
    items = by_name(score_case(case, output))
    assert items["source_hit"].score == 0.0
    assert items["retrieval_recall"].score == 1.0  # → a ranking/selection problem, not retrieval


# ---- case validation ---------------------------------------------------------------------

def test_validate_flags_unanswerable_expecting_answer():
    case = make_case(category="unanswerable", expected_behavior="answer", must_include=[["x"]])
    assert "unanswerable cases must expect insufficient_evidence" in validate_case(case)


def test_validate_flags_verified_case_with_null_fact():
    case = make_case(facts=[{"label": "x", "value": None, "unit": "%"}])
    assert any("verified" in p for p in validate_case(case))


def test_years_alone_do_not_require_citations():
    case = make_case(must_include=[["毛利率"]])
    items = by_name(score_case(case, make_output("2025 年表現穩健。毛利率 59.1%[1]。", citations=[Citation(1)])))
    assert items["citation_coverage"].score == 1.0
