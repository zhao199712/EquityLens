"""Client parsing, judge parsing and the run → rescore → compare pipeline, all offline."""

import json

import httpx

from equitylens_eval.cli import main
from equitylens_eval.client import EquityLensClient
from equitylens_eval.judge import JudgeConfig, LlmJudge, parse_judgement
from equitylens_eval.schema import Citation, EvalCase, RunOutput

CASE = EvalCase.from_dict({
    "id": "2330-gm", "category": "numeric_lookup", "ticker": "2330", "question": "毛利率？", "verified": True,
    "request": {"sourcePolicy": "LocalOnly"},
    "facts": [{"label": "毛利率", "value": 59.1, "unit": "%"}],
    "sources": [{"document_type": "AnnualReport", "page": 45}],
    "reference_answer": "2025 年毛利率為 59.1%。",
})

ASK_BODY = {
    "question": "毛利率？", "answer": "2025 年毛利率為 59.1%[1]。", "model": "gemini-x", "status": "Answered",
    "researchRunId": "11111111-1111-1111-1111-111111111111",
    "retrievalStrategy": {"mode": "Auto", "searches": []},
    "citations": [{"index": 1, "sourceType": "LocalDocument", "documentType": "AnnualReport", "pageNumber": 45,
                   "title": "2025 年報", "quoteText": "毛利率 59.1%", "relevanceScore": 0.9, "sourceRole": "Primary"}],
    "trace": {"tokenUsage": {"model": "gemini-x", "promptTokens": 1200, "completionTokens": 150, "totalTokens": 1350},
              "latencyMs": {"search": 100, "rerank": 50, "generation": 900, "total": 1050},
              "results": [{"documentType": "AnnualReport", "pageNumber": 45, "documentTitle": "2025 年報",
                           "rankAfterRerank": 1, "selected": True}]},
}


def test_ask_mode_sends_case_request_and_parses_trace():
    seen = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["path"] = request.url.path
        seen["body"] = json.loads(request.content)
        seen["auth"] = request.headers.get("authorization")
        return httpx.Response(200, json=ASK_BODY)

    with EquityLensClient("http://api", token="t0k", transport=httpx.MockTransport(handler)) as client:
        output = client.run(CASE, "ask")
    assert seen["path"] == "/api/research/ask"
    assert seen["body"]["sourcePolicy"] == "LocalOnly" and seen["body"]["debug"] is True
    assert seen["auth"] == "Bearer t0k"
    assert output.status == "Answered" and output.citations[0].page == 45
    assert output.prompt_tokens == 1200 and output.latency_ms == 1050
    assert output.candidates[0].selected


def test_investigation_mode_polls_until_terminal():
    polls = {"n": 0}

    def handler(request: httpx.Request) -> httpx.Response:
        path = request.url.path
        if path == "/api/research/investigations":
            return httpx.Response(202, json={"agentRunId": "a1", "researchRunId": "r1",
                                             "workflowType": "ResearchInvestigation", "status": "Pending"})
        if path == "/api/agent-runs/a1":
            polls["n"] += 1
            status = "Running" if polls["n"] < 3 else "Succeeded"
            return httpx.Response(200, json={"run": {"id": "a1", "status": status, "totalInputTokens": 9000,
                                                     "totalOutputTokens": 800, "totalEstimatedCostUsd": 0.012}})
        if path == "/api/research/runs/r1":
            return httpx.Response(200, json={
                "run": {"status": "Answered", "latencyMs": 42000},
                "answer": "毛利率 59.1%[1]",
                "citations": [{"citationIndex": 1, "sourceType": "LocalDocument", "documentType": "AnnualReport",
                               "pageNumber": 45, "title": "2025 年報", "quoteText": "59.1%"}],
                "candidates": [{"documentType": "AnnualReport", "pageNumber": 45, "title": "2025 年報",
                                "rankAfterRerank": 1, "decision": "Selected"}],
            })
        return httpx.Response(404)

    with EquityLensClient("http://api", poll_interval_s=0, transport=httpx.MockTransport(handler)) as client:
        output = client.run(CASE, "investigation")
    assert polls["n"] == 3
    assert output.cost_usd == 0.012 and output.latency_ms == 42000 and output.candidates[0].selected


def test_http_errors_become_error_outputs_instead_of_crashing():
    transport = httpx.MockTransport(lambda request: httpx.Response(400, json={"code": "ticker_not_supported"}))
    with EquityLensClient("http://api", transport=transport) as client:
        output = client.run(CASE, "ask")
    assert output.status == "Error" and "HTTP 400" in output.error


def test_judge_parses_json_and_normalizes_scores():
    reply = '好的：{"correctness": 5, "groundedness": 3, "completeness": 4, "unsupported_claims": ["x"], "rationale": "ok"}'
    assert parse_judgement(reply)["groundedness"] == 3

    def handler(request: httpx.Request) -> httpx.Response:
        body = json.loads(request.content)
        assert body["temperature"] == 0
        return httpx.Response(200, json={"choices": [{"message": {"content": reply}}]})

    judge = LlmJudge(JudgeConfig("http://judge", "k", "judge-model"), transport=httpx.MockTransport(handler))
    output = RunOutput("2330-gm", "ask", "Answered", "毛利率 59.1%[1]", citations=[Citation(1, quote="59.1%")])
    correctness, groundedness = judge.judge(CASE, output)
    assert correctness.score == 1.0 and groundedness.score == 0.5
    assert correctness.details["prompt_version"] == 1


def _write_responses(path, answer):
    output = RunOutput("2330-gm", "ask", "Answered", answer,
                       citations=[Citation(1, document_type="AnnualReport", page=45)], latency_ms=1000,
                       prompt_tokens=1000, completion_tokens=100)
    path.write_text(json.dumps(output.to_dict(), ensure_ascii=False) + "\n", encoding="utf-8")


def _verified_cases_file(tmp_path):
    cases = tmp_path / "cases.jsonl"
    cases.write_text(json.dumps({
        "id": "2330-gm", "category": "numeric_lookup", "ticker": "2330", "question": "毛利率？", "verified": True,
        "facts": [{"label": "毛利率", "value": 59.1, "unit": "%"}],
        "sources": [{"document_type": "AnnualReport", "page": 45}],
    }, ensure_ascii=False) + "\n", encoding="utf-8")
    return cases


def test_rescore_and_compare_report_regressions(tmp_path, capsys):
    cases = _verified_cases_file(tmp_path)
    good_raw, bad_raw = tmp_path / "raw-v4", tmp_path / "raw-v5"
    good_raw.mkdir(), bad_raw.mkdir()
    _write_responses(good_raw / "responses.jsonl", "毛利率 59.1%[1]")
    _write_responses(bad_raw / "responses.jsonl", "毛利率 56.1%[1]")

    assert main(["rescore", "--responses", str(good_raw / "responses.jsonl"), "--cases", str(cases),
                 "--label", "prompt-v4", "--no-judge"]) == 0
    assert main(["rescore", "--responses", str(bad_raw / "responses.jsonl"), "--cases", str(cases),
                 "--label", "prompt-v5", "--no-judge"]) == 0
    good, bad = tmp_path / "prompt-v4", tmp_path / "prompt-v5"
    summary = json.loads((good / "summary.json").read_text(encoding="utf-8"))
    assert summary["metrics"]["pass_rate"]["mean"] == 1.0
    assert (bad / "report.md").read_text(encoding="utf-8").count("2330-gm") >= 1

    capsys.readouterr()
    assert main(["compare", str(good), str(bad)]) == 0
    report = capsys.readouterr().out
    assert "**Regressions (1)**: 2330-gm" in report
    assert "⚠️" in report


def test_rescore_keeps_system_provenance_and_never_overwrites_the_original_run(tmp_path):
    cases = _verified_cases_file(tmp_path)
    original = tmp_path / "baseline"
    original.mkdir()
    _write_responses(original / "responses.jsonl", "毛利率 59.1%[1]")
    (original / "run_meta.json").write_text(json.dumps({"mode": "ask", "system_commit": "abc1234"}), encoding="utf-8")

    # writing into the run's own directory is refused
    assert main(["rescore", "--responses", str(original / "responses.jsonl"), "--cases", str(cases),
                 "--label", "baseline", "--no-judge"]) == 2
    assert not (original / "summary.json").exists()

    assert main(["rescore", "--responses", str(original / "responses.jsonl"), "--cases", str(cases),
                 "--label", "baseline-rescored", "--no-judge"]) == 0
    meta = json.loads((tmp_path / "baseline-rescored" / "summary.json").read_text(encoding="utf-8"))["meta"]
    assert meta["system_commit"] == "abc1234"  # where the answers came from
    assert "scorer_commit" in meta  # which scoring code judged them
    assert meta["rescored_from"] == str(original.resolve())


def test_client_waits_out_rate_limits_then_succeeds():
    calls, waits = {"n": 0}, []

    def handler(request: httpx.Request) -> httpx.Response:
        calls["n"] += 1
        if calls["n"] <= 2:
            return httpx.Response(429, headers={"Retry-After": "7"}, json={"code": "rate_limited"})
        return httpx.Response(200, json=ASK_BODY)

    with EquityLensClient("http://api", transport=httpx.MockTransport(handler), sleep=waits.append) as client:
        output = client.run(CASE, "ask")

    assert output.status == "Answered"
    assert waits == [7.0, 7.0]


def test_client_gives_up_after_repeated_rate_limits():
    waits = []
    transport = httpx.MockTransport(lambda request: httpx.Response(429, headers={"Retry-After": "600"}))

    with EquityLensClient("http://api", transport=transport, sleep=waits.append) as client:
        output = client.run(CASE, "ask")

    assert output.status == "Error" and "HTTP 429" in output.error
    assert waits == [60.0, 60.0, 60.0]  # Retry-After is capped
