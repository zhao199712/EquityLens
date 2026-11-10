"""Adversarial and replay checks; no model/network or production services."""
import copy
import json
from types import SimpleNamespace

import pytest

from equitylens_eval import conversation as conv, conversation_cli as cli, conversation_scoring as scoring
from equitylens_eval.conversation_research import evaluate, aggregate, load_rubric
from test_conversation_runtime import CASES, RESEARCH, execute

EPS = '2330-eps-2025'
MARGIN = '2330-gross-margin-2025'


def citation(index=1, page=10, quote='114年度 基本每股盈餘 66.26；稀釋每股盈餘 66.25'):
    return dict(citationIndex=index, documentType='AnnualReport', pageNumber=page,
                title='2330 Annual Report 2025', quoteText=quote,
                documentId='bbefd357-8e17-4a8d-a3da-e9b85a6780bc')


def row(answer='台積電 2025 年基本 EPS 為 66.26 元。[1]', citations=None):
    return dict(status='completed', answer=answer, research=dict(answer=answer,
                citations=[citation()] if citations is None else citations))


def value(answer, citations=None, refs=None):
    return evaluate(row(answer, citations), refs or [EPS], RESEARCH)[0]


@pytest.mark.parametrize('page', [10, 58])
def test_reviewed_equivalent_pages_support_eps(page):
    assert value('台積電 2025 年基本 EPS 66.26 元。[1]', [citation(page=page)]) is True


@pytest.mark.parametrize('page', [9, 11, 57, 59])
def test_other_pages_are_not_accepted_by_loose_tolerance(page):
    assert value('台積電 2025 年基本 EPS 66.26 元。[1]', [citation(page=page)]) is False


@pytest.mark.parametrize('answer', [
    '鴻海 2025 年基本 EPS 66.26 元。[1]',
    '台積電 2024 年基本 EPS 66.26 元。[1]',
    '台積電 2025 年第四季基本 EPS 66.26 元。[1]',
    '台積電 2025 年基本 EPS 66.25 元。[1]',
    '台積電 2025 年基本 EPS 66.26 美元。[1]',
    '台積電 2025 年稀釋 EPS 66.26 元。[1]',
    '台積電 2025 年基本 EPS 66.26 元。[1]\n台積電 2025 年基本 EPS 45.25 元。[1]',
    '台積電 2025 年基本 EPS 45.25 元。[1] 毛利率為 66.26%。[1]',
    '台積電 2025 年基本 EPS 未提供；別的指標為 66.26 元。[1]',
])
def test_same_number_is_not_enough_and_contradictions_fail(answer):
    assert value(answer) is False


def test_previous_year_and_diluted_eps_are_separate_metrics():
    assert value('台積電 2025 年基本 EPS 66.26 元，2024 年為 45.25 元。[1]') is True
    assert value('台積電 2025 年基本 EPS 66.26 元，稀釋 EPS 66.25 元。[1]') is True


def test_heading_scope_and_relative_year():
    assert value('## 台積電 2025 年財報\n基本 EPS 66.26 元，前一年為 45.25 元。[1]') is True
    assert value('## 鴻海 2024 年財報\n基本 EPS 66.26 元。[1]') is False


def test_missing_quote_is_unknown_but_wrong_quote_or_identity_is_failure():
    c = citation(); c.pop('quoteText')
    assert value('基本 EPS 66.26 元。[1]', [c]) is None
    c['quoteText'] = '114年度 無關內容 66.26'
    assert value('基本 EPS 66.26 元。[1]', [c]) is False
    c = citation(); c['documentId'] = 'another-document'
    assert value('基本 EPS 66.26 元。[1]', [c]) is False


def test_citation_must_belong_to_the_claim_not_just_exist_in_list():
    assert value('基本 EPS 66.26 元。[2]', [citation()]) is False
    assert value('基本 EPS 66.26 元。', [citation()]) is False
    quotes = [citation(1, 9, '營業毛利 2281293979；營業收入 3809054272'), citation(2)]
    assert value('毛利率59.9%[2]，基本 EPS66.26元[1]。', quotes, [MARGIN, EPS]) is False
    assert value('毛利率59.9%，基本 EPS66.26元。[1][2]', quotes, [MARGIN, EPS]) is True


def test_empty_answer_and_refusal_are_failures_not_missing_telemetry():
    for text in ['', '目前提供的資料不足以回答此問題。', '只談展望，沒有數據。']:
        result, parts = evaluate(row(text, []), [EPS], RESEARCH)
        assert result is False
        assert any(p['check'] == 'research_fact' and p['status'] == 'fail' for p in parts)
        assert any(p['status'] == 'unverifiable' for p in parts)
    assert evaluate({'status': 'completed'}, [EPS], RESEARCH)[0] is None


def test_captured_answer_can_fail_even_when_research_artifact_missing():
    assert evaluate({'answer': '目前提供的資料不足以回答此問題。'}, [EPS], RESEARCH)[0] is False


def test_ambiguous_number_binding_never_automatically_passes():
    assert value('66.26 元。[1]') is None


def test_fail_dominates_unknown_in_any_order():
    assert aggregate([None, False]) is False
    assert aggregate([False, None, True]) is False
    assert aggregate([True, None]) is None
    assert aggregate([]) is None


def test_reported_integer_is_distinct_from_wrong_calculated_precision():
    c = citation(page=9, quote='營業毛利 2281293979；營業收入 3809054272')
    assert value('台積電2025年毛利率59.9%，財報表列整數60%。[1]', [c], [MARGIN]) is True
    assert value('台積電2025年毛利率表列整數60%。[1]', [c], [MARGIN]) is True
    assert value('台積電2025年毛利率依金額相除約60.0%。[1]', [c], [MARGIN]) is False


def test_swapped_table_metrics_do_not_pass_by_global_number_presence():
    ref = '2317-segment-mix'
    source = dict(citationIndex=1, documentType='AnnualReport', pageNumber=112,
                  title='2317 Annual Report 2025', quoteText='消費智能 3081686 雲端網路 3255827 電腦終端 1228391 元件及其他 537201 合計8103105 2001817')
    answer = ('## 鴻海 2025 年\n| 產品 | 營收占比 | 年增率 |\n|---|---|---|\n'
              '| 消費智能 | 38.0%[1] | -2.8%[1] |\n| 雲端網路 | 40.2%[1] | 62.6%[1] |\n'
              '| 電腦終端 | 15.2%[1] | -2.7%[1] |\n| 元件及其他 | 6.6%[1] | 26.4%[1] |')
    assert value(answer, [source], [ref]) is True
    assert value(answer.replace('40.2%[1] | 62.6%', '62.6%[1] | 40.2%'), [source], [ref]) is False
    assert value(answer.replace('62.6%', '62.7%'), [source], [ref]) is False


def test_product_gate_is_observation_not_independent_correctness():
    system, raw, _ = execute(CASES[0])
    board = json.loads(raw['turns'][-1]['detail']['blackboardJson'])
    board['portfolioQualityGate'] = {'passed': False, 'blockingIssues': ['UntraceableNumbers']}
    raw['turns'][-1]['detail']['blackboardJson'] = board
    report = scoring.score_case(CASES[0], raw, system.entry, system.frozen, RESEARCH)
    assert not any(c['check'] == 'output_gate' for c in report['checks'])
    assert report['product_gates'][-1]['status'] == 'rejected'
    assert report['status'] == 'unverifiable'
    summary = scoring.summarize([report])
    assert summary['product_gate_counts']['rejected'] == 1
    assert summary['independent_check_counts']['fail'] == 0
    assert summary['conversation_success_rate'] is None


def test_accepted_product_gate_does_not_override_wrong_research_fact():
    system, raw, _ = execute(CASES[1])
    raw['turns'][0].update(row('台積電2025年基本 EPS66.25元。[1]'))
    raw['turns'][0]['detail']['blackboardJson'] = {'outputGate': {'passed': True}}
    report = scoring.score_case(CASES[1], raw, system.entry, system.frozen, RESEARCH)
    assert report['product_gates'][0]['status'] == 'accepted'
    assert any(c['check'] == 'research_grounded' and c['status'] == 'fail' for c in report['checks'])


def test_report_pins_rubric_and_generates_pending_review_not_fake_judgments(tmp_path):
    system, raw, _ = execute(CASES[0])
    m = dict(cases={CASES[0]['id']: system.entry}, fixture=system.frozen)
    report = cli.report(tmp_path, [CASES[0]], [raw], [m], RESEARCH)
    assert json.loads((tmp_path/'scoring_rubric.json').read_text())['version'] == 2
    queue = json.loads((tmp_path/'review_queue.json').read_text())
    assert queue and all(x['status'] == 'unreviewed' and x['verdict'] is None for x in queue)
    assert all(len(x['response_sha256']) == 64 for x in queue)
    assert report['conversation_success_rate'] is None

@pytest.mark.parametrize('answer', [
    '鴻海2024年財報\n基本 EPS66.26元。[1]',
    '基本 EPS66.26元（鴻海2024年）。[1]',
])
def test_plain_intro_and_postfixed_scope_cannot_borrow_requested_identity(answer):
    assert value(answer) is False


def test_rubric_is_not_silently_applied_to_a_different_oracle():
    changed = copy.deepcopy(RESEARCH)
    changed[EPS]['facts'][0]['value'] = 99
    result, parts = evaluate(row('基本EPS99元。[1]'), [EPS], changed)
    assert result is None
    assert parts[0]['check'] == 'research_oracle'


def test_invalid_rubric_is_rejected_before_execution(tmp_path):
    from equitylens_eval.conversation_research import load_rubric
    p = tmp_path/'invalid.json'; p.write_text('{"version":99}')
    with pytest.raises(ValueError, match='rubric'):
        load_rubric(p)


def test_saved_rubric_is_reused_and_tampering_rejected(tmp_path, monkeypatch):
    from equitylens_eval import conversation_seed as seed
    system, raw, _ = execute(CASES[0])
    source = tmp_path/'source'; source.mkdir()
    manifest = cli.public_manifest(dict(cases={CASES[0]['id']:system.entry},fixture=system.frozen))
    for name, data in [('cases',[CASES[0]]),('research',RESEARCH),('fixtures',[manifest])]:
        seed.private_json(source/(name+'.json'),data)
    seed.private_json(source/'run_meta.json',dict(system_commit='old',cases_hash=seed.digest([CASES[0]]),
        research_hash=seed.digest(RESEARCH),fixtures_hash=seed.digest([manifest])))
    (source/'responses.jsonl').write_text(json.dumps(raw)+'\n')
    original=cli.report(source,[CASES[0]],[raw],[manifest],RESEARCH)
    monkeypatch.setattr(cli,'git_sha',lambda:'scorer-sha')
    import httpx
    monkeypatch.setattr(httpx.Client,'request',lambda *a,**k:pytest.fail('network'))
    again=cli.rescore(SimpleNamespace(responses=source/'responses.jsonl',out=tmp_path/'again'))
    assert again==original
    rubric=json.loads((source/'scoring_rubric.json').read_text());rubric['cases'][EPS]['metrics'][0]['pages'].append(11)
    (source/'scoring_rubric.json').write_text(json.dumps(rubric))
    with pytest.raises(ValueError,match='rubric changed'):
        cli.rescore(SimpleNamespace(responses=source/'responses.jsonl',out=tmp_path/'tampered'))
    assert not (tmp_path/'tampered').exists()


def test_source_number_must_support_basic_eps_not_diluted_eps():
    c = citation(quote='114年度 基本每股盈餘45.25；稀釋每股盈餘66.26')
    assert value('台積電2025年基本EPS66.26元。[1]', [c]) is False


@pytest.mark.parametrize('quote', [
    '114年度、113年度\n| 基本每股盈餘 | $ 66.26 | | $ 45.25 | |',
    '114年度113年度基本每股盈餘$66.26$45.25稀釋每股盈餘$66.25$45.25',
])
def test_eps_quote_preserves_adjacent_table_column_boundaries(quote):
    assert value('基本 EPS66.26元。[1]', [citation(quote=quote)]) is True


def test_eps_quote_does_not_accept_numeric_prefix():
    assert value('基本 EPS66.26元。[1]', [citation(quote='114年度 基本每股盈餘66.261')]) is False
