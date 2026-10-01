import json

import httpx
import pytest

from equitylens_eval.cli import build_record
from equitylens_eval.judge import JudgeConfig, LlmJudge
from equitylens_eval.report import compare, render_markdown, summarize
from equitylens_eval.schema import Citation, EvalCase, RunOutput


def numeric_case(value, unit="元", **extra):
    return EvalCase.from_dict({
        "id": "signed-fact", "category": "numeric_lookup", "ticker": "2330", "question": "EPS？",
        "verified": True, "facts": [{"label": "EPS", "value": value, "unit": unit}], **extra,
    })


def answer(text):
    return RunOutput("signed-fact", "ask", "Answered", text, citations=[Citation(1, quote="原文")])


@pytest.mark.parametrize("expected,text", [
    (-3.2, "EPS +3.2 元[1]"), (3.2, "EPS -3.2 元[1]"),
    (-0.01, "年增 +0.01%[1]"), (0.01, "年增 −0.01%[1]"),
])
def test_opposite_signs_fail_even_inside_absolute_tolerance(expected, text):
    case = numeric_case(expected, "%" if "%" in text else "元")
    record = build_record(case, answer(text), None)
    assert record["scores"]["facts"] == 0.0
    assert record["passed"] is False


@pytest.mark.parametrize("sign", ["-", "－", "−"])
def test_negative_sign_characters_preserve_the_value(sign):
    record = build_record(numeric_case(-3.2), answer(f"EPS {sign}3.2 元[1]"), None)
    assert record["passed"] is True


@pytest.mark.parametrize("expected,unit,text", [
    (3.2, "元", "EPS +3.2 元[1]"),
    (-28943, "億元", "營收 -2.89 兆元[1]"),
    (0, "%", "年增 -0.01%[1]"),
    (-0.01, "%", "年增 0%[1]"),
])
def test_units_rounding_and_zero_keep_existing_tolerances(expected, unit, text):
    assert build_record(numeric_case(expected, unit), answer(text), None)["passed"] is True


def test_direction_words_do_not_implicitly_negate_numbers():
    record = build_record(numeric_case(-1.2, "百分點"), answer("下降 1.2 個百分點[1]"), None)
    assert record["passed"] is False


def judge_case(**extra):
    return EvalCase.from_dict({
        "id": "judge", "category": "qualitative", "ticker": "2330", "question": "風險？",
        "verified": True, "must_include": [["風險"]], "reference_answer": "主要風險是匯率。", **extra,
    })


def failed_judge_response(request, failure):
    if failure == "503":
        return httpx.Response(503)
    if failure == "timeout":
        raise httpx.ReadTimeout("judge timed out", request=request)
    if failure == "bad_json":
        return httpx.Response(200, text="not json")
    if failure == "empty_choices":
        return httpx.Response(200, json={"choices": []})
    content = {
        "invalid_verdict": "not a verdict", "missing_score": '{"correctness": 5}',
        "null_score": '{"correctness": null}', "null_content": None,
    }[failure]
    return httpx.Response(200, json={"choices": [{"message": {"content": content}}]})


@pytest.mark.parametrize("failure", [
    "503", "timeout", "bad_json", "empty_choices", "invalid_verdict", "missing_score", "null_score", "null_content",
])
def test_judge_failures_cannot_pass_and_are_visible_in_reports(failure):
    judge = LlmJudge(JudgeConfig("http://judge/v1", "test", "test"),
                     httpx.MockTransport(lambda request: failed_judge_response(request, failure)))
    try:
        record = build_record(judge_case(), answer("主要風險是火星殖民失敗[1]"), judge)
    finally:
        judge.close()
    assert record["passed"] is False
    assert record["judge_error"]
    assert record["error"] is None  # the research API itself succeeded
    assert record["scores"]["judge_correctness"] is None
    summary = summarize([record], {"label": "judge-failure"})
    assert summary["metrics"]["pass_rate"] == {"mean": 0.0, "n": 1}
    assert summary["operational"]["judge_error_count"] == 1  # both judge scores share one failed request
    assert summary["operational"]["error_rate"] == 0.0
    assert "judge error:" in render_markdown(summary, [record])


def test_disabled_and_inapplicable_judges_do_not_fail_cases():
    assert build_record(judge_case(), answer("主要風險是匯率[1]"), None)["passed"] is True
    def unexpected_call(request):
        raise AssertionError("inapplicable cases should not call the judge")
    judge = LlmJudge(JudgeConfig("http://judge/v1", "test", "test"), httpx.MockTransport(unexpected_call))
    try:
        record = build_record(judge_case(reference_answer=None), answer("主要風險是匯率[1]"), judge)
        refusal = RunOutput("judge", "ask", "InsufficientEvidence", "資料不足")
        refused = build_record(judge_case(expected_behavior="insufficient_evidence", must_include=[]), refusal, judge)
    finally:
        judge.close()
    assert record["passed"] is True and record["judge_error"] is None
    assert refused["passed"] is True and refused["judge_error"] is None


def test_draft_with_judge_error_still_has_no_pass_verdict():
    judge = LlmJudge(JudgeConfig("http://judge/v1", "test", "test"),
                     httpx.MockTransport(lambda request: httpx.Response(503)))
    try:
        record = build_record(judge_case(verified=False), answer("主要風險是匯率[1]"), judge)
    finally:
        judge.close()
    assert record["passed"] is None and record["judge_error"]
    summary = summarize([record], {})
    assert summary["verified_case_count"] == 0
    assert "judge error:" in render_markdown(summary, [record])


def test_successful_judge_keeps_scores_and_no_error():
    verdict = '{"correctness":5,"groundedness":3,"completeness":5}'
    judge = LlmJudge(JudgeConfig("http://judge/v1", "test", "test"), httpx.MockTransport(
        lambda request: httpx.Response(200, json={"choices": [{"message": {"content": verdict}}]})))
    try:
        record = build_record(judge_case(), answer("主要風險是匯率[1]"), judge)
    finally:
        judge.close()
    assert record["passed"] is True and record["judge_error"] is None
    assert record["scores"]["judge_correctness"] == 1.0
    assert record["scores"]["judge_groundedness"] == 0.5


def test_older_reports_without_judge_fields_can_be_compared(tmp_path):
    record = build_record(judge_case(), answer("主要風險是匯率[1]"), None)
    legacy_record = {k: v for k, v in record.items() if k != "judge_error"}
    assert summarize([legacy_record], {})["operational"]["judge_error_count"] == 0
    baseline, candidate = tmp_path / "old", tmp_path / "new"
    for path, label in [(baseline, "old"), (candidate, "new")]:
        path.mkdir()
        summary = summarize([legacy_record], {"label": label})
        if path == baseline:
            del summary["operational"]["judge_error_count"]
        else:
            summary["operational"]["judge_error_count"] = 1
        (path / "summary.json").write_text(json.dumps(summary), encoding="utf-8")
        (path / "results.jsonl").write_text(json.dumps(legacy_record) + "\n", encoding="utf-8")
    assert "| judge_error_count | 0 | 1 | +1.000 ⚠️ |" in compare(baseline, candidate)
