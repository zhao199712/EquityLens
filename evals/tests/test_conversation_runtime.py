import copy
import json
import math
from pathlib import Path
from types import SimpleNamespace
import uuid

import httpx
import pytest

from equitylens_eval import conversation as conv
from equitylens_eval import conversation_cli as cli
from equitylens_eval import conversation_runner as runner
from equitylens_eval import conversation_seed as seed
from equitylens_eval import conversation_scoring as scoring
from equitylens_eval.portfolio import independent_metrics

CASES = conv.read_jsonl(conv.CASE_PATH)
SPEC = conv.load_fixture()
RESEARCH = conv._research_index(conv.RESEARCH_PATH)
DAY = '2026-11-04'


def entry():
    return {owner: dict(id=str(uuid.uuid4()), email=f'{owner}@example.invalid', password='private-secret',
                        portfolios={ref: str(uuid.uuid4()) for ref in SPEC['owners'][owner]['portfolios']})
            for owner in ('owner_a', 'owner_b')}


def comparison(portfolio, pid, constraints=None):
    snapshot = copy.deepcopy(portfolio['snapshot'])
    snapshot['portfolioId'] = pid
    snapshot['from'], snapshot['to'] = snapshot['dates'][0], snapshot['dates'][-1]

    def scenario(weights, mode='cash', target=None):
        metrics = independent_metrics(snapshot, weights)
        return dict(weights=weights, quantities={a['ticker']: weights[a['ticker']]*portfolio['nav']/a['valuationPrice']
                                                for a in snapshot['assets']},
                    cashWeight=1-sum(weights.values()), remainderPolicy=mode, transferToTicker=target,
                    status='Calculated' if metrics is not None else 'InsufficientHistory', metrics=metrics)
    baseline = scenario(dict(portfolio['weights']))
    constraints = constraints or dict(mode='cash', source_ticker=next(iter(portfolio['weights'])), target_weight=.1)
    golden = seed.scenario_golden(portfolio, constraints)
    s = scenario(golden['weights'], constraints['mode'], constraints.get('transfer_to'))
    if s['metrics']:
        s['differenceFromBaseline'] = {k: v-baseline['metrics'][k] for k, v in s['metrics'].items()}
    return dict(snapshot=snapshot, baseline=baseline, scenarios=[s])


class FakeSystem:
    """Mock the API boundary, including approvals, SSE and persisted observer data."""
    def __init__(self, case, identities=None, *, failure=None):
        self.case = case
        self.entry = identities or entry()
        self.frozen = seed.build_fixture(SPEC, case['fixture'], DAY)
        self.session = str(uuid.uuid4())
        self.index = -1
        self.turns = []
        self.runs = {}
        self.details = {}
        self.approved = set()
        self.sent = []
        self.cancelled = []
        self.failure = failure
        self.snapshots = 0

    def preflight(self):
        pass

    def snapshot(self):
        self.snapshots += 1
        return dict(holdings=[{'quantity': 1}], transactions=[], cash_flows=[])

    def capture(self, session, request_id=None):
        turns = [t for t in self.turns if request_id is None or t['request_id'] == request_id]
        return copy.deepcopy(dict(session={'id': session, 'user_id': self.entry['owner_a']['id'],
                                            'context': {}, 'version': len(self.turns)},
                                  turns=turns, runs=[self.runs[t['agent_run_id']] for t in turns if t.get('agent_run_id')]))

    def handle(self, request):
        path = request.url.path
        payload = json.loads(request.content) if request.content else {}
        if path == '/api/auth/login':
            return httpx.Response(200, json={'user': {'id': self.entry['owner_a']['id']}, 'accessToken': 'private-token'})
        if path == '/api/chat/sessions':
            return httpx.Response(200, json={'id': self.session})
        if path.endswith('/messages'):
            self.index += 1
            self.sent.append(payload)
            turn = self.case['turns'][self.index]
            expected = turn['expected']
            action = {'clarify': 'AskClarification', 'reset': 'ResetContext'}.get(expected['intent'], 'DirectResponse')
            events = []
            record = {'id': str(uuid.uuid4()), 'request_id': payload['requestId'], 'action': action,
                      'chat_session_id': self.session, 'status': 'Succeeded'}
            if conv.INTENTS[expected['intent']]['creates_run']:
                rid = str(uuid.uuid4())
                workflow = scoring.WORKFLOWS[conv.INTENTS[expected['intent']]['workflow']]
                pid = self.entry['owner_a']['portfolios'].get(expected.get('portfolio_ref'))
                inp = {'portfolioId': pid} if pid else {'ticker': expected['ticker']}
                self.runs[rid] = dict(id=rid, user_id=self.entry['owner_a']['id'], input=inp,
                                      workflow=workflow, research_run_id=None)
                record['agent_run_id'] = rid
                board = {}
                tools = []
                if pid:
                    context = {'portfolioId': pid}
                    board = dict(portfolioContext=context, portfolioQualityGate={'passed': True})
                    tools = [dict(toolName='get_portfolio_snapshot', status='Succeeded',
                                  resultJson=json.dumps({'data': {'diagnosis': context}}))]
                    if expected['intent'] in ('risk_reduction', 'insufficient_data'):
                        board['portfolioScenarioComparison'] = comparison(
                            self.frozen['portfolios'][expected['portfolio_ref']], pid, expected.get('constraints'))
                self.details[rid] = dict(run=dict(id=rid, workflowType=workflow, status='Succeeded'), nodes=[],
                                        blackboardJson=json.dumps(board), outputJson=json.dumps({'summary': '測試回答'}),
                                        toolCalls=tools, approvals=[])
                if self.failure == 'approval' or self.failure == 'unsafe_approval':
                    self.details[rid]['approvals'] = [dict(id='approval', status='Pending', scope='WorkflowGate',
                        sideEffectLevel='ReadOnly' if self.failure == 'approval' else 'ExternalWrite',
                        nodeKey='approve', nodeType='WaitForHumanApproval')]
                    self.details[rid]['run']['status'] = 'AwaitingApproval'
                if self.failure == 'timeout':
                    self.details[rid]['run']['status'] = 'Running'
                if self.failure == 'run_failed':
                    self.details[rid]['run']['status'] = 'Failed'
                action = record['action'] = 'RouteWorkflow'
                events.append({'type': 'run_created', 'runCard': {'agentRunId': rid}})
            self.turns.append(record)
            events.insert(0, {'type': 'conversation_action', 'action': action})
            events.append({'type': 'clarification' if action == 'AskClarification' else 'delta', 'content': '測試回答'})
            events.append({'type': 'done'})
            if self.failure == 'sse':
                events = [{'type': 'error', 'message': 'failure'}]
            if self.failure == 'incomplete':
                events.pop()
            data = ''.join('data: '+json.dumps(e)+'\n\n' for e in events)
            return httpx.Response(200, content=data, headers={'Content-Type': 'text/event-stream'})
        if path.endswith('/approve'):
            rid = path.split('/')[3]
            self.approved.add(rid)
            self.details[rid]['approvals'][0]['status'] = 'Approved'
            self.details[rid]['run']['status'] = 'Succeeded'
            return httpx.Response(200, json={})
        if path.endswith('/cancel'):
            self.cancelled.append(path.split('/')[3])
            return httpx.Response(204)
        if '/run-cards/' in path:
            return httpx.Response(200, json={'finalAnswer': '測試回答'})
        if path.startswith('/api/agent-runs/'):
            return httpx.Response(200, json=self.details[path.split('/')[3]])
        raise AssertionError(f'Unexpected API request: {request.method} {path}')


def execute(case, **kwargs):
    system = FakeSystem(case, **kwargs)
    checkpoints = []
    with httpx.Client(base_url='http://localhost', transport=httpx.MockTransport(system.handle)) as client:
        auth = runner.AuthClient(client, system.entry['owner_a'])
        raw = runner.run_case(auth, system, case, timeout=.02 if kwargs.get('failure') == 'timeout' else 1,
                              poll_interval=0, checkpoint=lambda r: checkpoints.append(copy.deepcopy(r)))
    return system, raw, checkpoints


@pytest.mark.parametrize('case', CASES, ids=lambda c: c['id'])
def test_all_45_turns_execute_through_http_and_preserve_user_messages(case):
    system, raw, checkpoints = execute(case)
    assert len(raw['turns']) == case['max_user_turns']
    assert all(r['status'] == 'completed' for r in raw['turns'])
    assert [p['content'] for p in system.sent] == [t['user'] for t in case['turns']]
    assert all(set(p) == {'content', 'requestId'} for p in system.sent)
    assert len({r['session_id'] for r in raw['turns']}) == 1
    assert raw['before'] == raw['after']
    assert checkpoints
    result = scoring.score_case(case, raw, system.entry, system.frozen, RESEARCH)
    assert result['conversation_success'] is None
    assert not any(c['status'] == 'fail' for c in result['checks'])
    assert all(c['status'] == 'unverifiable' for c in result['checks']
               if conv.CHECKS.get(c['check'], {}).get('scorer') == 'semantic')


@pytest.mark.parametrize('cid', ['journey-00', 'journey-04', 'journey-10'])
def test_selected_portfolio_switch_and_reset_workflows(cid):
    case = next(c for c in CASES if c['id'] == cid)
    system, raw, _ = execute(case)
    scored = scoring.score_case(case, raw, system.entry, system.frozen, RESEARCH)
    assert all(c['status'] == 'pass' for c in scored['checks']
               if c['check'] in ('workflow', 'action', 'resource_binding', 'selection_retained'))
    if cid == 'journey-10':
        assert any(c['check'] == 'reset_boundary' and c['status'] == 'unverifiable' for c in scored['checks'])


@pytest.mark.parametrize('failure', ['sse', 'incomplete', 'timeout', 'unsafe_approval', 'run_failed'])
def test_failed_execution_is_saved_and_later_turns_not_run(failure):
    # Use a portfolio first turn so timeout/approval failures occur before the last turn.
    case = copy.deepcopy(CASES[0])
    case['turns'] = [dict(turn='t1', user='組合甲波動率？', expected=case['turns'][2]['expected']),
                     dict(turn='t2', user='那組合乙呢？', expected=case['turns'][2]['expected'])]
    system, raw, checkpoints = execute(case, failure=failure)
    assert raw['turns'][0]['status'] == 'failed'
    assert raw['turns'][1]['status'] == 'not_run'
    assert raw['after'] is not None
    assert checkpoints[0]['turns'][0]['events']
    if failure in ('timeout', 'unsafe_approval'):
        assert system.cancelled
    if failure == 'unsafe_approval':
        assert not system.approved


def test_only_readonly_workflow_gate_is_approved_and_draft_saved():
    system, raw, _ = execute(CASES[0], failure='approval')
    last = raw['turns'][-1]
    assert last['status'] == 'completed'
    assert last['approvals'][0]['run']['status'] == 'AwaitingApproval'
    assert system.approved


def test_sse_multiline_comments_and_malformed_input():
    events = list(runner.parse_sse([': keepalive', 'data: {"type":', 'data: "done"}', '']))
    assert events == [{'type': 'done'}]
    with pytest.raises(ValueError):
        list(runner.parse_sse(['data: nope', '']))


@pytest.mark.parametrize('profile,count', [('standard', 253), ('no_direct', 253), ('short_history', 61)])
def test_seed_reproducible_shared_prices_and_independent_baseline(profile, count):
    frozen = seed.build_fixture(SPEC, profile, DAY)
    assert seed.digest(frozen) == seed.digest(seed.build_fixture(SPEC, profile, DAY))
    assert len(frozen['dates']) == count
    for portfolio in frozen['portfolios'].values():
        assert abs(portfolio['nav']-100000) < .001
        for a in portfolio['snapshot']['assets']:
            assert a['prices'] == frozen['prices'][a['ticker']]
        assert (portfolio['metrics'] is None) == (profile == 'short_history')
    if profile == 'no_direct':
        assert '2330' not in frozen['portfolios']['alpha']['weights']


def test_independent_oracle_against_hand_calculated_alternating_returns():
    # 120 observations alternating +10%/-10%, sample stdev and nearest-rank tail.
    prices = [100.]
    for i in range(120):
        prices.append(prices[-1]*(1.1 if i % 2 == 0 else .9))
    metrics = independent_metrics({'dates': list(range(121)), 'assets': [{'ticker': 'A', 'prices': prices}]}, {'A': 1.})
    assert metrics['annualizedVolatility'] == pytest.approx(math.sqrt(120*.01/119)*math.sqrt(252))
    assert metrics['historicalVaR'] == pytest.approx(-.1)
    assert metrics['expectedShortfall'] == pytest.approx(-.1)
    assert metrics['hypotheticalMaxDrawdown'] == pytest.approx(.99**60/1.1-1)


def test_seed_sql_uses_one_security_per_ticker_and_no_deletes():
    frozen = seed.build_fixture(SPEC, 'standard', DAY)
    ids = {t: str(uuid.uuid4()) for t in SPEC['securities']}
    statement = seed.seed_sql(SPEC, frozen, {'journey-00': entry()}, ids)
    assert statement.count('INSERT INTO security(') == 3
    assert statement.count('INSERT INTO portfolio(') == 4
    assert 'DELETE' not in statement and 'UPDATE' not in statement
    assert "'TWSE'" in statement and "'ETF'" in statement
    assert statement.startswith('BEGIN;') and statement.endswith('COMMIT;')


def test_private_manifest_exclusive_and_redacted(tmp_path):
    manifest = dict(cases={'journey-00': entry()}, api_url='http://localhost', db={'username': 'hidden'},
                    securities={}, fixture={}, fixture_hash='hash')
    path = tmp_path/'private.json'
    seed.private_json(path, manifest)
    assert path.stat().st_mode & 0o777 == 0o600
    with pytest.raises(FileExistsError):
        seed.private_json(path, manifest)
    public = json.dumps(cli.public_manifest(manifest))
    assert 'private-secret' not in public
    assert 'password' not in public and 'email' not in public and 'username' not in public


def test_wrong_id_anywhere_in_scope_fails_even_when_answer_names_correct_portfolio():
    system, raw, _ = execute(CASES[0])
    last = raw['turns'][-1]
    last['answer'] = '這是組合甲的正確答案'
    last['detail']['toolCalls'][0]['resultJson'] = json.dumps({
        'data': {'diagnosis': {'portfolioId': system.entry['owner_b']['portfolios']['decoy_alpha']}}})
    result = scoring.score_case(CASES[0], raw, system.entry, system.frozen, RESEARCH)
    assert result['status'] == 'fail'
    assert any(c['check'] == 'owner_isolation' and c['status'] == 'fail' for c in result['checks'])
    assert any(c['check'] == 'resource_binding' and c['status'] == 'fail' for c in result['checks'])


def test_missing_scope_is_unverifiable_and_side_effect_is_failure():
    system, raw, _ = execute(CASES[0])
    raw['turns'][-1]['detail']['toolCalls'] = []
    raw['after']['holdings'][0]['quantity'] = 2
    result = scoring.score_case(CASES[0], raw, system.entry, system.frozen, RESEARCH)
    assert any(c['check'] == 'resource_binding' and c['status'] == 'unverifiable' for c in result['checks'])
    assert any(c['check'] == 'no_holdings_mutation' and c['status'] == 'fail' for c in result['checks'])


def test_trace_and_semantic_checks_never_default_to_pass():
    system, raw, _ = execute(CASES[1])
    raw['turns'][0].pop('after')
    result = scoring.score_case(CASES[1], raw, system.entry, system.frozen, RESEARCH)
    assert next(c for c in result['checks'] if c['check'] == 'trace_complete')['status'] == 'unverifiable'
    assert scoring.summarize([result])['conversation_success_rate'] is None


def comparison_row(profile='standard', constraints=None):
    frozen = seed.build_fixture(SPEC, profile, DAY)
    portfolio = frozen['portfolios']['alpha']
    comp = comparison(portfolio, 'portfolio', constraints)
    return {'detail': {'blackboardJson': {'portfolioScenarioComparison': comp}}}, portfolio, comp


def test_correct_scenario_passes_independent_recalculation():
    constraints = dict(mode='holding', source_ticker='2330', target_weight=.3, transfer_to='0050')
    row, portfolio, _ = comparison_row(constraints=constraints)
    assert scoring.comparison_check(row, portfolio, constraints)[0] is True


@pytest.mark.parametrize('mutate', [
    lambda c: c['snapshot']['assets'][0]['prices'].__setitem__(2, 900),
    lambda c: c['scenarios'][0]['metrics'].__setitem__('annualizedVolatility', 0),
    lambda c: c['scenarios'][0]['differenceFromBaseline'].__setitem__('historicalVaR', 99),
    lambda c: c['scenarios'][0].__setitem__('cashWeight', -.1),
    lambda c: c['scenarios'][0]['weights'].__setitem__('2330', float('nan')),
    lambda c: c['scenarios'][0]['quantities'].__setitem__('2330', 0),
])
def test_bad_math_prices_or_conservation_fails(mutate):
    row, portfolio, comp = comparison_row()
    mutate(comp)
    assert scoring.comparison_check(row, portfolio, None)[0] is False


def test_constraint_conditions_cannot_be_satisfied_by_different_scenarios():
    target = dict(mode='holding', source_ticker='2330', target_weight=.3, transfer_to='0050')
    row, portfolio, comp = comparison_row(constraints=dict(mode='cash', source_ticker='2330', target_weight=.3))
    other = comparison(portfolio, 'portfolio', dict(mode='holding', source_ticker='2330', target_weight=.2, transfer_to='0050'))
    comp['scenarios'].extend(other['scenarios'])
    assert scoring.comparison_check(row, portfolio, target)[0] is False


def test_short_history_is_unverifiable_unless_false_metrics_are_reported():
    row, portfolio, comp = comparison_row('short_history')
    assert scoring.comparison_check(row, portfolio, None)[0] is None
    comp['scenarios'][0]['status'] = 'Calculated'
    comp['scenarios'][0]['metrics'] = {'annualizedVolatility': .1}
    assert scoring.comparison_check(row, portfolio, None)[0] is False


@pytest.mark.parametrize('mutate,match', [
    (lambda c, f: c[0].update(turns=[], max_user_turns=0), 'empty'),
    (lambda c, f: c[0].update(case_checks=[]), 'required'),
    (lambda c, f: c[0]['turns'][0]['expected'].update(checks=[]), 'empty'),
    (lambda c, f: f['profiles']['standard'].update(price_points=-1), 'price_points'),
    (lambda c, f: f['profiles']['standard']['weights']['alpha'].update({'2330': float('nan')}), 'finite'),
    (lambda c, f: f['profiles']['standard']['weights']['alpha'].update({'2330': True}), 'finite'),
])
def test_contract_rejects_empty_and_nonfinite_inputs(mutate, match):
    cases, spec = copy.deepcopy(CASES), copy.deepcopy(SPEC)
    mutate(cases, spec)
    with pytest.raises(ValueError, match=match):
        conv.validate_cases(cases, spec, RESEARCH)


def test_offline_rescore_uses_saved_contract_and_no_network(tmp_path, monkeypatch):
    system, raw, _ = execute(CASES[0])
    source, output = tmp_path/'original', tmp_path/'rescored'
    source.mkdir()
    m = dict(cases={CASES[0]['id']: system.entry}, fixture=system.frozen)
    saved = cli.public_manifest(m)
    seed.private_json(source/'fixtures.json', [saved])
    seed.private_json(source/'cases.json', [CASES[0]])
    seed.private_json(source/'research.json', RESEARCH)
    seed.private_json(source/'run_meta.json', dict(system_commit='old-api-sha', research_hash=seed.digest(RESEARCH),
                                                        cases_hash=seed.digest([CASES[0]]), fixtures_hash=seed.digest([saved])))
    (source/'responses.jsonl').write_text(json.dumps(raw)+'\n')
    original = cli.report(source, [CASES[0]], [raw], [saved], RESEARCH)
    monkeypatch.setattr(httpx.Client, 'request', lambda *a, **k: pytest.fail('rescore used network'))
    monkeypatch.setattr(cli, 'git_sha', lambda: 'new-scorer-sha')
    rerun = cli.rescore(SimpleNamespace(responses=source/'responses.jsonl', out=output))
    assert rerun == original
    assert json.loads((output/'run_meta.json').read_text())['system_commit'] == 'old-api-sha'
    assert json.loads((output/'score_meta.json').read_text())['scorer_commit'] == 'new-scorer-sha'
    with pytest.raises(FileExistsError):
        cli.rescore(SimpleNamespace(responses=source/'responses.jsonl', out=source))


def test_reused_session_fails_suite_check(tmp_path):
    system, raw, _ = execute(CASES[0])
    second = copy.deepcopy(CASES[0])
    second['id'] = 'journey-99'
    second_raw = copy.deepcopy(raw)
    second_raw['case_id'] = second['id']
    m = dict(cases={CASES[0]['id']: system.entry, second['id']: system.entry}, fixture=system.frozen)
    report = cli.report(tmp_path, [CASES[0], second], [raw, second_raw], [m], RESEARCH)
    assert report['failed'] == 2
    assert all(any(c['check'] == 'same_session' and c['status'] == 'fail' for c in r['checks'])
               for r in report['results'])


def test_live_requires_external_budget_and_never_accepts_silent_draft_run(tmp_path, monkeypatch):
    monkeypatch.delenv('CONVERSATION_EVAL_BUDGET_ENFORCED', raising=False)
    args = SimpleNamespace(timeout=1, usage_ledger=tmp_path/'absent')
    with pytest.raises(ValueError, match='budget'):
        cli.run(args, CASES, SPEC, RESEARCH)


class SeedDatabase:
    config = dict(container='fixture-db', database='journey_eval', username='eval')
    def __init__(self, *, duplicate=False, dirty=False, fail=False):
        self.identities = []
        self.writes = []
        self.duplicate, self.dirty, self.fail = duplicate, dirty, fail
    def bind(self, user):
        self.identities.append(user['id'])
    def json(self, query):
        if self.duplicate:
            return [dict(id=str(uuid.uuid4()), exchange='TWSE', currency='TWD', asset_type='Stock')]*2
        return []
    def sql(self, query, *, write=False):
        if write:
            self.writes.append(query)
            if self.fail:
                raise RuntimeError('write failed')
            return ''
        return '1' if self.dirty else '0'


def seed_args(tmp_path, **kwargs):
    return SimpleNamespace(base_url='http://localhost', container='fixture-db', database='journey_eval',
                           username='eval', manifest=tmp_path/'private.json', profile='standard',
                           as_of=seed.utc_day(), corpus=None, **kwargs)


def register_client(request):
    assert request.url.path == '/api/auth/register'
    return httpx.Response(200, json={'user': {'id': str(uuid.uuid4())}, 'accessToken': 'secret-token'})


def test_seed_registers_distinct_case_accounts_and_commits_one_profile(tmp_path):
    db = SeedDatabase()
    with httpx.Client(base_url='http://localhost', transport=httpx.MockTransport(register_client)) as client:
        manifest = seed.seed(seed_args(tmp_path), CASES, SPEC, db=db, client=client)
    assert manifest['state'] == 'ready'
    assert len(manifest['cases']) == 9
    assert len(set(db.identities)) == 18
    assert len(db.writes) == 1
    assert manifest['numeric_verification'] == 'independent-python-v1'
    assert 'secret-token' not in Path(seed_args(tmp_path).manifest).read_text()
    # Explicit constraints have oracle values; free-form reductions have no arbitrary target.
    assert 't4' in manifest['cases']['journey-06']['goldens']
    assert not manifest['cases']['journey-00']['goldens']


@pytest.mark.parametrize('problem', ['duplicate', 'dirty', 'manifest_exists'])
def test_seed_rejects_reuse_before_registering_accounts(tmp_path, problem):
    args = seed_args(tmp_path)
    if problem == 'manifest_exists':
        args.manifest.write_text('{}')
    db = SeedDatabase(duplicate=problem == 'duplicate', dirty=problem == 'dirty')
    with httpx.Client(base_url='http://localhost', transport=httpx.MockTransport(
            lambda request: pytest.fail('registration occurred before isolation guard'))) as client:
        with pytest.raises((ValueError, FileExistsError)):
            seed.seed(args, CASES, SPEC, db=db, client=client)
    assert not db.writes


def test_seed_keeps_partial_manifest_after_failed_transaction(tmp_path):
    db = SeedDatabase(fail=True)
    args = seed_args(tmp_path)
    with httpx.Client(base_url='http://localhost', transport=httpx.MockTransport(register_client)) as client:
        with pytest.raises(RuntimeError):
            seed.seed(args, CASES, SPEC, db=db, client=client)
    assert json.loads(args.manifest.read_text())['state'] == 'seeding'


def test_corpus_requires_matching_reviewed_file_and_complete_embeddings():
    did = str(uuid.uuid4())
    proof = dict(version='review-v1', case_refs=['2330-eps-2025'], documents={did: 'file-hash'})
    row = dict(id=did, sha256_hash='file-hash', chunks=2, embeddings=2, content_fingerprint='text-and-vector')
    db = SimpleNamespace(json=lambda _: [row])
    original = seed.corpus_state(db, proof)
    row['content_fingerprint'] = 'different-vector'
    assert original['fingerprint'] != seed.corpus_state(db, proof)['fingerprint']
    row['embeddings'] = 1
    with pytest.raises(ValueError, match='corpus'):
        seed.corpus_state(db, proof)
    assert seed.corpus_state(db, None) is None


def test_database_reads_are_transactionally_read_only(monkeypatch):
    monkeypatch.setattr(seed.subprocess, 'check_output', lambda *a, **k: json.dumps([
        {'Config': {'Labels': {'com.docker.compose.project': 'equitylens-conversation-eval'}}}]))
    observed = []
    def run(args, **kwargs):
        observed.append((args, kwargs['input']))
        return SimpleNamespace(returncode=0, stdout='[]')
    monkeypatch.setattr(seed.subprocess, 'run', run)
    db = seed.Database('db', 'journey_eval', 'eval')
    assert db.json('SELECT 1;') == []
    assert observed[0][1] == 'BEGIN READ ONLY;\nSELECT 1;\nCOMMIT;'
    with pytest.raises(ValueError, match='_eval'):
        seed.Database('db', 'production', 'eval')


def test_price_preflight_detects_extra_history_and_adjusted_price_drift():
    frozen = seed.build_fixture(SPEC, 'short_history', DAY)
    ticker = next(iter(frozen['prices']))
    rows = [dict(date=day, close=p, adjusted_close=p, interval='1d', data_source='conversation-eval-v1')
            for day, p in zip(frozen['dates'], frozen['prices'][ticker])]
    m = dict(securities={ticker: str(uuid.uuid4())}, fixture=frozen)
    db = SimpleNamespace(json=lambda _: rows)
    cli.verify_prices(db, m)
    rows.append(dict(rows[-1]))
    with pytest.raises(ValueError, match='changed'):
        cli.verify_prices(db, m)
    rows.pop()
    rows[-1]['adjusted_close'] += .1
    with pytest.raises(ValueError, match='changed'):
        cli.verify_prices(db, m)


def make_manifest(case, identities):
    frozen = seed.build_fixture(SPEC, case['fixture'], seed.utc_day())
    return dict(version=1, state='ready', suite=SPEC['suite'], profile=case['fixture'],
                api_url='http://localhost', db=SeedDatabase.config, as_of=seed.utc_day(),
                cases={case['id']: identities}, fixture=frozen, fixture_hash=seed.digest(frozen),
                spec_hash=seed.digest(SPEC), corpus=None, securities={})


def test_manifest_rejects_shared_databases_or_changed_fixture(tmp_path):
    first = make_manifest(CASES[0], entry())
    paths = [tmp_path/'a.json', tmp_path/'b.json']
    seed.private_json(paths[0], first)
    seed.private_json(paths[1], first)
    with pytest.raises(ValueError, match='distinct'):
        cli.load_manifests(paths, SPEC)
    first['fixture']['cash'] = 99
    paths[0].write_text(json.dumps(first))
    with pytest.raises(ValueError, match='hash'):
        cli.load_manifests(paths[:1], SPEC)


def test_cli_run_artifacts_and_offline_rescore(tmp_path, monkeypatch):
    system = FakeSystem(CASES[0])
    m = make_manifest(CASES[0], system.entry)
    manifest = tmp_path/'private.json'
    seed.private_json(manifest, m)
    ledger = tmp_path/'usage.jsonl'
    ledger.write_text('')
    monkeypatch.setenv('CONVERSATION_EVAL_BUDGET_ENFORCED', '1')
    monkeypatch.setattr(cli, 'Database', lambda **_: SeedDatabase())
    monkeypatch.setattr(runner, 'Observer', lambda *a: system)
    monkeypatch.setattr(cli, 'git_sha', lambda: 'test-harness')
    original_client = httpx.Client
    monkeypatch.setattr(httpx, 'Client', lambda **kwargs: original_client(
        **kwargs, transport=httpx.MockTransport(system.handle)))
    args = SimpleNamespace(timeout=1, usage_ledger=ledger, manifest=[manifest], case=['journey-00'],
                           include_unverified=True, out=tmp_path/'run', system_commit='test-api')
    summary = cli.run(args, CASES, SPEC, RESEARCH)
    assert summary['failed'] == 0
    assert summary['conversation_success_rate'] is None
    for path in args.out.glob('*.json*'):
        text = path.read_text()
        assert 'private-secret' not in text and 'private-token' not in text
    rescored = cli.rescore(SimpleNamespace(responses=args.out/'responses.jsonl', out=tmp_path/'rescore'))
    assert rescored == summary
    assert json.loads((args.out/'run_meta.json').read_text())['system_commit'] == 'test-api'


def test_missing_corpus_is_dependency_failure_without_chat_calls(tmp_path, monkeypatch):
    m = make_manifest(CASES[1], entry())
    path = tmp_path/'manifest.json'
    seed.private_json(path, m)
    ledger = tmp_path/'usage.jsonl'
    ledger.write_text('')
    monkeypatch.setenv('CONVERSATION_EVAL_BUDGET_ENFORCED', '1')
    monkeypatch.setattr(cli, 'Database', lambda **_: SeedDatabase())
    monkeypatch.setattr(httpx, 'Client', lambda **_: pytest.fail('model/chat API must not be called'))
    args = SimpleNamespace(timeout=1, usage_ledger=ledger, manifest=[path], case=['journey-01'],
                           include_unverified=True, out=tmp_path/'run', system_commit='test-api')
    summary = cli.run(args, CASES, SPEC, RESEARCH)
    assert summary['failure_counts']['dependency_failed'] > 0
    assert summary['failure_counts']['bug'] == 0


def test_partial_stream_created_run_is_cancelled_even_if_db_capture_fails():
    system = FakeSystem(CASES[0], failure='incomplete')
    # Use the diagnosis as first turn so the first partial stream creates a run.
    system.case = copy.deepcopy(CASES[0])
    system.case['turns'] = [system.case['turns'][-1]]
    def broken_capture(session, request_id=None):
        if request_id:
            raise RuntimeError('DB temporarily unavailable')
        return {'session': {'id': session}, 'turns': [], 'runs': []}
    system.capture = broken_capture
    with httpx.Client(base_url='http://localhost', transport=httpx.MockTransport(system.handle)) as client:
        raw = runner.run_case(runner.AuthClient(client, system.entry['owner_a']), system, system.case)
    assert system.cancelled
    assert raw['turns'][0]['capture_error']


def test_verified_unresolved_cases_do_not_inflate_success_denominator():
    rows = [dict(case_id='a', review_status='verified', status='pass', conversation_success=True, checks=[]),
            dict(case_id='b', review_status='verified', status='unverifiable', conversation_success=None, checks=[])]
    report = scoring.summarize(rows)
    assert report['conversation_denominator'] == 2
    assert report['conversation_success_rate'] is None


def test_provider_error_and_wrong_session_owner_fail_independently():
    system, raw, _ = execute(CASES[0])
    raw['provider_errors'] = ['unsettled_provider_call']
    raw['turns'][0]['after']['session']['user_id'] = system.entry['owner_b']['id']
    report = scoring.score_case(CASES[0], raw, system.entry, system.frozen, RESEARCH)
    assert any(c['check'] == 'owner_isolation' and c['status'] == 'fail' for c in report['checks'])
    assert any(c['check'] == 'provider_health' and c['failure_class'] == 'dependency_failed' for c in report['checks'])


def test_research_check_uses_existing_numeric_and_source_oracles():
    refs = ['2330-gross-margin-2025', '2330-eps-2025']
    research = dict(answer='毛利率 59.9%[1]，EPS 66.26元[2]。', run={'status': 'Answered'}, citations=[
        dict(citationIndex=1, documentType='AnnualReport', pageNumber=9, title='2330 Annual Report 2025'),
        dict(citationIndex=2, documentType='AnnualReport', pageNumber=58, title='2330 Annual Report 2025')])
    row = dict(research=research, detail={'run': {'status': 'Succeeded'}})
    assert scoring.research_check(row, refs, RESEARCH)[0] is True
    research['answer'] = research['answer'].replace('66.26', '66.25')
    assert scoring.research_check(row, refs, RESEARCH)[0] is False


def test_malformed_comparison_evidence_is_unverifiable_without_crashing_report():
    case = CASES[1]
    system, raw, _ = execute(case)
    board = json.loads(raw['turns'][-1]['detail']['blackboardJson'])
    board['portfolioScenarioComparison']['scenarios'][0]['weights'] = None
    raw['turns'][-1]['detail']['blackboardJson'] = board
    report = scoring.score_case(case, raw, system.entry, system.frozen, RESEARCH)
    assert any(c['check'] == 'computed_comparison' and c['status'] != 'pass' for c in report['checks'])


def test_terminal_failed_run_is_not_cancelled_again():
    system, raw, _ = execute(CASES[0], failure='run_failed')
    assert raw['turns'][-1]['error'] == 'run_failed'
    assert not system.cancelled and not raw['turns'][-1].get('cancel_error')


def test_short_history_full_metrics_fail_even_without_computed_comparison_check():
    case = next(c for c in CASES if c['id'] == 'journey-09')
    system, raw, _ = execute(case)
    board = json.loads(raw['turns'][-1]['detail']['blackboardJson'])
    board['portfolioScenarioComparison']['scenarios'][0]['metrics'] = {'annualizedVolatility': .1}
    raw['turns'][-1]['detail']['blackboardJson'] = board
    report = scoring.score_case(case, raw, system.entry, system.frozen, RESEARCH)
    assert any(c['check'] == 'insufficient_history' and c['status'] == 'fail' for c in report['checks'])


def test_wrongly_attributed_turn_does_not_establish_complete_trace():
    system, raw, _ = execute(CASES[0])
    raw['turns'][0]['after']['turns'][0]['request_id'] = str(uuid.uuid4())
    report = scoring.score_case(CASES[0], raw, system.entry, system.frozen, RESEARCH)
    assert next(c for c in report['checks'] if c['check'] == 'trace_complete')['status'] == 'unverifiable'


def test_unsettled_existing_ledger_rejected_before_model_calls(tmp_path, monkeypatch):
    ledger = tmp_path/'usage.jsonl'
    ledger.write_text(json.dumps(dict(id='pending', reserved_usd=.1))+'\n')
    monkeypatch.setenv('CONVERSATION_EVAL_BUDGET_ENFORCED', '1')
    args = SimpleNamespace(timeout=1, usage_ledger=ledger)
    with pytest.raises(ValueError, match='unsettled'):
        cli.run(args, CASES, SPEC, RESEARCH)


def test_verified_case_with_unknown_checks_keeps_unknown_success():
    case = copy.deepcopy(CASES[0])
    case['review_status'] = 'verified'
    system, raw, _ = execute(case)
    report = scoring.score_case(case, raw, system.entry, system.frozen, RESEARCH)
    assert report['status'] == 'unverifiable'
    assert report['conversation_success'] is None
