import json

import pytest

from equitylens_eval.client import EquityLensClient, parse_trajectory
from equitylens_eval.cli import build_record
from equitylens_eval.report import summarize
from equitylens_eval.schema import EvalCase, RunOutput


def case():
    return EvalCase.from_dict(dict(id="policy", category="qualitative", ticker="2330", question="資料？",
                                   verified=True, expected_behavior="insufficient_evidence",
                                   request={"sourcePolicy": "LocalOnly", "engine": "DynamicDag"}))


def test_engine_override_preserves_case_request():
    original = case()
    assert EquityLensClient.build_request(original, False, "AgentLoop")["engine"] == "AgentLoop"
    assert original.request["engine"] == "DynamicDag"
    assert EquityLensClient.build_request(original, False)["sourcePolicy"] == "LocalOnly"


@pytest.mark.parametrize("status,decision,expected", [("Denied", "Deny", 0), ("Succeeded", "Allow", 1),
                                                        ("Failed", "Allow", 1), ("Running", "Allow", 1)])
def test_web_dispatch_includes_failures_but_excludes_guard_denials(status, decision, expected):
    trajectory = parse_trajectory({"toolCalls": [{"toolName": "search_web", "status": status, "guardDecision": decision}]})
    assert trajectory["web_calls"] == expected
    assert trajectory["guard_denials"] == (expected == 0)


def test_legacy_skip_is_not_a_web_call_and_remediation_is_not_double_counted():
    trajectory = parse_trajectory({"nodes": [
        {"nodeType": "RetrieveWebResearchEvidence", "status": "Succeeded", "outputJson": '{"used":false}'},
        {"nodeType": "RetrieveWebEvidence", "status": "Succeeded"},
    ], "toolCalls": [{"toolName": "braveWebSearch", "status": "Succeeded"}]})
    assert trajectory["web_calls"] == 1
    assert parse_trajectory({"nodes": [{"nodeType": "RetrieveWebResearchEvidence", "outputJson": '{"used":true}'}]})["web_calls"] == 1


def test_policy_violation_fails_otherwise_correct_refusal_and_is_aggregated():
    output = RunOutput("policy", "investigation", "InsufficientEvidence", "資料不足", trajectory={"web_calls": 1,
                       "planner_modes": ["Llm", "DeterministicFallback"], "llm_turns": 2})
    record = build_record(case(), output, None)
    assert record["passed"] is False
    assert record["scores"]["source_policy_compliance"] == 0
    summary = summarize([record], {})
    assert summary["trajectory"]["web_policy_violation_count"] == 1
    assert summary["trajectory"]["planner_fallback_rate"] == .5
    assert summary["trajectory"]["mean_llm_turns"] == 2


def test_old_output_has_unknown_trajectory_not_invented_zero():
    output = RunOutput("policy", "ask", "InsufficientEvidence", "資料不足")
    record = build_record(case(), output, None)
    assert record["passed"] is True
    assert record["scores"]["source_policy_compliance"] is None
    assert summarize([record], {})["trajectory"]["web_policy_violation_count"] is None


def test_trajectory_parser_tolerates_bad_event_payloads():
    result = parse_trajectory({"events": [{"eventType": "PlannerProposed", "payloadJson": x}
                                         for x in ["not json", "[]", "null", '{"mode":"DeterministicFallback"}']]})
    assert result["planner_modes"] == ["DeterministicFallback"]
