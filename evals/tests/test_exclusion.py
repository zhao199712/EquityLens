"""Cases with a known-bad corpus (excluded_reason) must never count, even on rescore."""

from equitylens_eval.cli import _select, build_record
from equitylens_eval.report import render_markdown, summarize
from equitylens_eval.schema import EvalCase, RunOutput, load_cases, validate_case
from equitylens_eval.scorers import case_passed, score_case

REASON = "2454 財報擷取有已知問題，尚未重新擷取"


def make_case(**overrides):
    raw = {"id": "c1", "category": "unanswerable", "ticker": "2454", "question": "q",
           "expected_behavior": "insufficient_evidence", "verified": True}
    raw.update(overrides)
    return EvalCase.from_dict(raw)


def refusal(case_id="c1"):
    return RunOutput(case_id=case_id, mode="ask", status="InsufficientEvidence",
                     answer="目前提供的資料不足以回答此問題。")


def test_excluded_reason_is_parsed_and_not_counted():
    case = make_case(excluded_reason=REASON)
    assert case.excluded_reason == REASON
    assert case.verified and not case.counted
    assert validate_case(case) == []


def test_excluded_case_never_passes_even_when_behaviour_is_right():
    case = make_case(excluded_reason=REASON)
    assert case_passed(case, score_case(case, refusal())) is None
    counted = make_case()
    assert case_passed(counted, score_case(counted, refusal())) is True


def test_select_skips_excluded_unless_requested():
    cases = [make_case(id="a"), make_case(id="b", excluded_reason=REASON)]
    assert [c.id for c in _select(cases, False, None, None)] == ["a"]
    assert [c.id for c in _select(cases, False, None, None, include_excluded=True)] == ["a", "b"]


def test_rescore_style_records_put_excluded_cases_outside_pass_rate():
    counted, excluded = make_case(id="a"), make_case(id="b", excluded_reason=REASON)
    records = [build_record(counted, refusal("a"), None), build_record(excluded, refusal("b"), None)]
    summary = summarize(records, {"mode": "ask"})
    assert summary["verified_case_count"] == 1
    assert summary["excluded_cases"] == ["b"]
    assert summary["metrics"]["pass_rate"]["mean"] == 1.0
    report = render_markdown(summary, records)
    assert "Excluded cases" in report and REASON in report


def test_research_v1_marks_corpus_problem_cases_as_excluded():
    from pathlib import Path
    cases = load_cases(Path(__file__).resolve().parent.parent / "cases" / "research_v1.jsonl")
    excluded = {c.id for c in cases if c.excluded_reason}
    assert excluded == {"2454-revenue-2025", "2412-cash-dividend-2025", "2454-false-premise-quantum"}
    assert sum(1 for c in cases if c.counted) == 8
