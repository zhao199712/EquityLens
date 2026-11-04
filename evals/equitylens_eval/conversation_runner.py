"""Journey transport and evidence collection. Expectations never enter requests."""
from __future__ import annotations

import json
import time
import uuid

import httpx

from .portfolio import obj
from .conversation_seed import q


class ExecutionStopped(RuntimeError):
    pass


class AuthClient:
    """Reauthenticate on 401 only; never retry an accepted model request."""
    def __init__(self, client, user):
        self.client, self.user = client, user
        self.login()

    def login(self):
        r = self.client.post('/api/auth/login', json={
            'email': self.user['email'], 'password': self.user['password']})
        r.raise_for_status()
        body = r.json()
        if body['user']['id'] != self.user['id']:
            raise ValueError('Login returned a different owner')
        self.client.headers['Authorization'] = 'Bearer ' + body['accessToken']

    def request(self, method, path, **kwargs):
        r = self.client.request(method, path, **kwargs)
        if r.status_code == 401:
            self.login()
            r = self.client.request(method, path, **kwargs)
        r.raise_for_status()
        return r

    def get(self, path):
        return self.request('GET', path).json()

    def post(self, path, payload=None):
        return self.request('POST', path, json=payload).json()

    def events(self, path, payload, timeout):
        # SSE is not retried: a dropped stream may already have created a run.
        self.login()
        with self.client.stream('POST', path, json=payload, timeout=timeout) as r:
            r.raise_for_status()
            yield from parse_sse(r.iter_lines())


def parse_sse(lines):
    """SSE framing supports comments and multi-line data; malformed data is an error."""
    data = []
    for line in lines:
        if line == '':
            if data:
                yield json.loads('\n'.join(data))
                data = []
        elif line.startswith('data:'):
            data.append(line[5:].lstrip(' '))
    if data:
        yield json.loads('\n'.join(data))


class Observer:
    def __init__(self, db, entry, frozen=None):
        self.db, self.entry, self.frozen = db, entry, frozen
        self.uid = str(uuid.UUID(entry['owner_a']['id']))

    def preflight(self):
        self.db.bind(self.entry['owner_a'])
        if self.db.sql(f"SELECT count(*) FROM chat_session WHERE user_id={q(self.uid)};") != '0':
            raise ValueError('Fixture account already used; seed fresh case accounts')
        for owner in ('owner_a', 'owner_b'):
            user = self.entry[owner]
            self.db.bind(user)
            ids = ','.join(q(str(uuid.UUID(p))) for p in user['portfolios'].values())
            count = self.db.sql(f"SELECT count(*) FROM portfolio WHERE owner_user_id={q(user['id'])} AND id IN ({ids});")
            if int(count) != len(user['portfolios']):
                raise ValueError('Portfolio ownership differs from manifest')
        if self.frozen is not None:
            state = self.snapshot()
            all_ids = {pid for owner in ('owner_a', 'owner_b') for pid in self.entry[owner]['portfolios'].values()}
            if {p['id'] for p in state['portfolios']} != all_ids:
                raise ValueError('Unexpected portfolio in fixture accounts')
            for owner in ('owner_a', 'owner_b'):
                for ref, pid in self.entry[owner]['portfolios'].items():
                    alias = ref.removeprefix('decoy_')
                    expected = self.frozen['portfolios'][alias]['snapshot']
                    rows = self.db.json(f"SELECT coalesce(jsonb_agg(x),'[]'::jsonb) FROM (SELECT s.ticker,h.quantity FROM portfolio_holding h JOIN security s ON s.id=h.security_id WHERE h.portfolio_id={q(pid)}) x;")
                    actual = {r['ticker']: float(r['quantity']) for r in rows}
                    if len(rows) != len(actual) or actual != {a['ticker']: a['quantity'] for a in expected['assets']}:
                        raise ValueError('Fixture holdings changed')
                    tx = [t for t in state['transactions'] if t['portfolio_id'] == pid]
                    flows = [f for f in state['cash_flows'] if f['portfolio_id'] == pid]
                    if (any(t['transaction_type'] != 'BUY' for t in tx)
                            or any(f['flow_type'] != 'Deposit' or f['status'] != 'Applied' or f['currency'] != 'TWD' for f in flows)):
                        raise ValueError('Unexpected fixture transaction/cash flow')
                    cash = sum(float(f['amount']) for f in flows) - sum(
                        float(t['quantity'])*float(t['price'])+float(t['fee']) for t in tx)
                    if abs(cash-expected['cash']) > .000001:
                        raise ValueError('Fixture cash changed')

    def snapshot(self):
        # All portfolios of BOTH owners, including newly created/unexpected portfolios.
        owners = ','.join(q(str(uuid.UUID(self.entry[o]['id']))) for o in ('owner_a', 'owner_b'))
        return self.db.json(f"""
SELECT jsonb_build_object(
 'portfolios',(SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY p.id),'[]') FROM portfolio p WHERE p.owner_user_id IN ({owners})),
 'holdings',(SELECT coalesce(jsonb_agg(to_jsonb(h) ORDER BY h.id),'[]') FROM portfolio_holding h JOIN portfolio p ON p.id=h.portfolio_id WHERE p.owner_user_id IN ({owners})),
 'transactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t.id),'[]') FROM portfolio_transaction t JOIN portfolio p ON p.id=t.portfolio_id WHERE p.owner_user_id IN ({owners})),
 'cash_flows',(SELECT coalesce(jsonb_agg(to_jsonb(f) ORDER BY f.id),'[]') FROM portfolio_cash_flow f JOIN portfolio p ON p.id=f.portfolio_id WHERE p.owner_user_id IN ({owners}))
);""")

    def capture(self, session, request_id=None):
        sid = q(str(uuid.UUID(session)))
        request = '' if request_id is None else f" AND t.request_id={q(str(uuid.UUID(request_id)))}"
        return self.db.json(f"""
SELECT jsonb_build_object(
 'session',(SELECT jsonb_build_object('id',s.id,'user_id',s.user_id,'context',s.context_json,'version',s.context_version)
   FROM chat_session s WHERE s.id={sid} AND s.user_id={q(self.uid)}),
 'turns',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t.created_at_utc),'[]')
   FROM conversation_turn t JOIN chat_session s ON s.id=t.chat_session_id
   WHERE s.id={sid} AND s.user_id={q(self.uid)}{request}),
 'runs',(SELECT coalesce(jsonb_agg(jsonb_build_object('id',r.id,'user_id',r.user_id,'input',r.input_json,
   'workflow',r.workflow_type,'research_run_id',r.research_run_id) ORDER BY r.id),'[]')
   FROM agent_run r WHERE r.id IN (
     SELECT t.agent_run_id FROM conversation_turn t JOIN chat_session s ON s.id=t.chat_session_id
     WHERE s.id={sid} AND s.user_id={q(self.uid)}{request}))
);""")


def wait_run(client, run_id, row, timeout, poll_interval):
    deadline = time.monotonic() + timeout
    approved = set()
    while time.monotonic() < deadline:
        detail = client.get(f'/api/agent-runs/{run_id}')
        row['detail'] = detail  # Preserve last observation even if polling fails.
        pending = [a for a in detail.get('approvals', []) if a.get('status') == 'Pending']
        for approval in pending:
            if not (detail['run'].get('workflowType') == 'PortfolioDiagnosis'
                    and approval.get('scope') == 'WorkflowGate'
                    and approval.get('sideEffectLevel') == 'ReadOnly'
                    and approval.get('nodeKey') == 'approvePortfolioDiagnosis'
                    and approval.get('nodeType') == 'WaitForHumanApproval'):
                raise ExecutionStopped('unsupported_approval')
            if approval['id'] not in approved:
                row.setdefault('approvals', []).append(detail)
                client.post(f"/api/agent-runs/{run_id}/approvals/{approval['id']}/approve",
                            {'requestId': str(uuid.uuid4()), 'comment': 'Isolated read-only conversation eval output review'})
                approved.add(approval['id'])
        status = detail['run']['status']
        if status in ('Succeeded', 'Failed', 'Cancelled'):
            if status != 'Succeeded':
                raise ExecutionStopped('run_' + status.lower())
            return
        time.sleep(min(poll_interval, max(0, deadline-time.monotonic())))
    raise TimeoutError('run_timeout')


def run_case(client, observer, case, *, timeout=360, poll_interval=1, checkpoint=lambda _: None):
    raw = dict(case_id=case['id'], turns=[], before=None, after=None)
    stopped = None
    session = None
    try:
        observer.preflight()
        raw['before'] = observer.snapshot()
        session = client.post('/api/chat/sessions', {'title': 'Conversation eval'})['id']
        raw['session_id'] = session
        initial = observer.capture(session)
        raw['initial'] = initial
        if initial.get('turns'):
            raise ExecutionStopped('session_not_fresh')
        for turn in case['turns']:
            row = dict(turn=turn['turn'], user=turn['user'], session_id=session,
                       request_id=str(uuid.uuid4()), events=[], status='running')
            raw['turns'].append(row)
            try:
                row['before'] = observer.capture(session)
                started = time.monotonic()
                for event in client.events(f'/api/chat/sessions/{session}/messages',
                                           {'content': turn['user'], 'requestId': row['request_id']}, timeout):
                    row['events'].append(event)
                    checkpoint(raw)
                    if event.get('type') == 'error':
                        raise ExecutionStopped('sse_error')
                    if time.monotonic()-started > timeout:
                        raise TimeoutError('stream_timeout')
                if not any(e.get('type') == 'done' for e in row['events']):
                    raise ExecutionStopped('incomplete_sse')
                row['after'] = observer.capture(session, row['request_id'])
                created = [e['runCard']['agentRunId'] for e in row['events'] if e.get('type') == 'run_created']
                persisted = [r['id'] for r in row['after'].get('runs', [])]
                row['run_ids'] = list(dict.fromkeys(created + persisted))
                if len(row['run_ids']) > 1:
                    raise ExecutionStopped('multiple_runs_for_turn')
                if row['run_ids']:
                    wait_run(client, row['run_ids'][0], row, max(.001, timeout-(time.monotonic()-started)), poll_interval)
                    # ResearchRunId may be assigned only after the run finishes.
                    row['after'] = observer.capture(session, row['request_id'])
                    research_id = next((r.get('research_run_id') for r in row['after'].get('runs', [])
                                        if r.get('research_run_id')), None)
                    if research_id:
                        row['research'] = client.get(f'/api/research/runs/{research_id}')
                    row['run_card'] = client.get(f"/api/chat/sessions/{session}/run-cards/{row['run_ids'][0]}")
                row['answer'] = answer_from(row)
                row['status'] = 'completed'
            except (httpx.HTTPError, TimeoutError, ValueError, KeyError, RuntimeError) as exc:
                stopped = str(exc) if isinstance(exc, ExecutionStopped) else type(exc).__name__
                row.update(status='failed', error=stopped)
                try:
                    row['after'] = observer.capture(session, row['request_id'])
                except (ValueError, RuntimeError):
                    row['capture_error'] = 'database_evidence_unavailable'
                # Do not allow a timed-out task to continue consuming budget unattended.
                run_ids = set(row.get('run_ids') or [])
                run_ids.update(r['id'] for r in row.get('after', {}).get('runs', []))
                run_ids.update(e['runCard']['agentRunId'] for e in row['events'] if e.get('type') == 'run_created')
                terminal = (row.get('detail') or {}).get('run', {}).get('status') in ('Succeeded', 'Failed', 'Cancelled')
                if not run_ids and (row.get('capture_error') or not row.get('after', {}).get('turns')
                                    or any(t.get('status') == 'Processing' for t in row.get('after', {}).get('turns', []))):
                    row['cancel_error'] = 'unknown_run_state'
                for rid in (() if terminal else run_ids):
                    try:
                        client.request('POST', f'/api/agent-runs/{rid}/cancel')
                    except (httpx.HTTPError, ValueError, RuntimeError):
                        row['cancel_error'] = 'cancellation_unconfirmed'
            checkpoint(raw)
            if stopped:
                break
    except (httpx.HTTPError, TimeoutError, ValueError, KeyError, RuntimeError) as exc:
        stopped = str(exc) if isinstance(exc, ExecutionStopped) else type(exc).__name__
        raw['error'] = stopped
    finally:
        try:
            raw['after'] = observer.snapshot()
        except (ValueError, RuntimeError):
            raw['snapshot_error'] = 'database_evidence_unavailable'
    seen = {r['turn'] for r in raw['turns']}
    for turn in case['turns']:
        if turn['turn'] not in seen:
            raw['turns'].append(dict(turn=turn['turn'], status='not_run',
                                     error='dependency_failed', dependency=stopped))
    return raw


def answer_from(row):
    detail = row.get('detail') or {}
    output = obj(detail.get('outputJson'))
    return ((row.get('research') or {}).get('answer') or output.get('interpretation')
            or output.get('summary') or ''.join(e.get('content', '') for e in row.get('events', [])
                                                if e.get('type') in ('delta', 'clarification')))
