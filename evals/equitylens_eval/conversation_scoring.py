"""Evidence-only journey scoring. Missing evidence is never a pass."""
from __future__ import annotations

import math

from .conversation import CAPABILITY_GAPS, CHECKS, INTENTS
from .portfolio import independent_metrics, obj
from .conversation_seed import scenario_golden

VERSION = 1
WORKFLOWS = {'Research': 'ResearchAgent', 'Portfolio': 'PortfolioDiagnosis'}


def result(check, value, evidence, reason='', failure_class=None):
    status = 'unverifiable' if value is None else 'pass' if value else 'fail'
    return dict(check=check, status=status, evidence=evidence, reason=reason,
                failure_class=failure_class if status != 'pass' else None)


def close(a, b, tolerance=1e-7):
    return (type(a) in (int, float) and type(b) in (int, float)
            and math.isfinite(a) and math.isfinite(b) and abs(a-b) <= tolerance)


def portfolio_ids(value):
    """Inspect structured evidence, including serialized JSON, never answer text."""
    if isinstance(value, str):
        if value.startswith(('{', '[')):
            try:
                return portfolio_ids(obj(value))
            except ValueError:
                pass
        return []
    if isinstance(value, list):
        return [p for item in value for p in portfolio_ids(item)]
    if isinstance(value, dict):
        return [v for k, v in value.items() if k in ('portfolioId', 'portfolio_id') and v] + [
            p for k, v in value.items() if k not in ('portfolioId', 'portfolio_id') for p in portfolio_ids(v)]
    return []


def structured(row):
    detail = row.get('detail') or {}
    return {'runs': row.get('after', {}).get('runs', []),
            'board': obj(detail.get('blackboardJson')), 'tools': detail.get('toolCalls', []),
            'nodes': detail.get('nodes', [])}


def binding(row, pid):
    data = structured(row)
    ids = portfolio_ids(data)
    if any(p != pid for p in ids):
        return False
    run_ids = portfolio_ids(data['runs'])
    board_ids = portfolio_ids(data['board'])
    scoped_tools = [c for c in data['tools'] if c.get('status') == 'Succeeded'
                    and c.get('toolName') in ('get_portfolio_snapshot', 'get_risk_report', 'run_risk_analysis',
                                             'get_performance_attribution', 'simulate_weight_scenario')]
    if not run_ids or not board_ids or not scoped_tools:
        return None
    # At least snapshot/risk evidence must reveal effective scope; no arguments is not proof.
    return True if any(portfolio_ids(c.get('resultJson')) for c in scoped_tools) else None


def comparison_check(row, portfolio, constraints):
    comparison = obj((row.get('detail') or {}).get('blackboardJson')).get('portfolioScenarioComparison')
    if not comparison:
        return None, 'No scenario comparison evidence'
    snapshot, baseline = comparison.get('snapshot'), comparison.get('baseline')
    scenarios = comparison.get('scenarios') or []
    if not snapshot or not baseline or not scenarios:
        return None, 'Comparison snapshot, baseline or scenarios missing'
    frozen = portfolio['snapshot']
    assets = {a['ticker']: a for a in snapshot.get('assets', [])}
    expected = {a['ticker']: a for a in frozen['assets']}
    if (len(assets) != len(snapshot.get('assets', [])) or set(assets) != set(expected)
            or snapshot.get('dates') != frozen['dates'] or not close(snapshot.get('cash'), frozen['cash'], .000001)
            or snapshot.get('priceBasis') != 'AdjustedClose'):
        return False, 'Snapshot differs from frozen seed'
    for ticker, asset in assets.items():
        ref = expected[ticker]
        if (not close(asset.get('quantity'), ref['quantity'], .000001)
                or not close(asset.get('valuationPrice'), ref['valuationPrice'], .000001)
                or len(asset.get('prices', [])) != len(ref['prices'])
                or any(not close(a, b, .000001) for a, b in zip(asset['prices'], ref['prices']))):
            return False, 'Snapshot prices/holdings differ from seed'
    if any(not close(baseline.get('weights', {}).get(k), v) for k, v in portfolio['weights'].items()):
        return False, 'Baseline weights differ from seed'
    nav = portfolio['nav']
    verified = 0
    for scenario in [baseline, *scenarios]:
        weights, quantities = scenario.get('weights') or {}, scenario.get('quantities') or {}
        cash = scenario.get('cashWeight')
        if (set(weights) != set(expected) or set(quantities) != set(expected)
                or any(type(v) not in (int, float) or not math.isfinite(v) or v < 0 for v in weights.values())
                or type(cash) not in (int, float) or not math.isfinite(cash) or cash < -1e-8
                or not close(sum(weights.values()) + cash, 1)):
            return False, 'Invalid allocation or cash conservation'
        if any(not close(quantities[k] * assets[k]['valuationPrice'], weights[k]*nav, .01) for k in weights):
            return False, 'Quantities and weights disagree'
        oracle = independent_metrics(frozen, weights)
        if oracle is None:
            if scenario.get('metrics') is not None or scenario.get('status') == 'Calculated':
                return False, 'Insufficient history produced a complete risk estimate'
            continue
        metrics = scenario.get('metrics') or {}
        if scenario.get('status') != 'Calculated' or any(not close(metrics.get(k), v) for k, v in oracle.items()):
            return False, 'Independent risk calculation differs'
        verified += 1
        if scenario is not baseline:
            delta = scenario.get('differenceFromBaseline') or {}
            if any(not close(delta.get(k), metrics[k]-baseline['metrics'][k]) for k in oracle):
                return False, 'Risk differences use another baseline'
    if constraints:
        golden = scenario_golden(portfolio, constraints)
        matches = [s for s in scenarios if s.get('remainderPolicy') == constraints['mode']
                   and s.get('transferToTicker') == constraints.get('transfer_to')
                   and all(close(s['weights'].get(k), v) for k, v in golden['weights'].items())
                   and close(s.get('cashWeight'), golden['cashWeight'])]
        if not matches:
            return False, 'No single scenario satisfies all requested constraints'
    if not verified:
        return None, 'History insufficient; allocation checked but risk comparison unverifiable'
    return True, 'Frozen prices, NAV, weights, risk math and same-basis differences verified'


def research_check(row, refs, research):
    if not row.get('research'):
        return None, 'Research answer/citations unavailable'
    from .client import parse_investigation
    from .schema import EvalCase
    from .scorers import score_facts, score_source_hit, score_citation_integrity
    items = []
    for ref in refs:
        case = EvalCase.from_dict(research[ref])
        output = parse_investigation(ref, row.get('detail', {}).get('run', {}), row['research'])
        items.extend(fn(case, output) for fn in (score_facts, score_source_hit, score_citation_integrity))
    if not items or any(i.score is None for i in items):
        return None, 'Required fact/source evidence unavailable'
    return all(i.score == 1 for i in items), 'Verified numeric facts, expected source hit and citation indices only'


def score_case(case, raw, entry, frozen, research):
    checks = []
    rows = {r['turn']: r for r in raw.get('turns', [])}
    owned = set(entry['owner_a']['portfolios'].values())
    uid = entry['owner_a']['id']

    def add(name, value, path, reason='', classification='bug'):
        checks.append(result(name, value, path, reason, classification))

    all_rows = [rows.get(t['turn'], {}) for t in case['turns']]
    if raw.get('provider_errors'):
        add('provider_health', False, 'responses/' + case['id'] + '/provider_errors',
            'Provider/admission failure; not an answer-quality failure', 'dependency_failed')
    if raw.get('fixture_error'):
        add('fixture_integrity', False, 'responses/' + case['id'] + '/fixture_error',
            'Frozen inputs changed during execution', 'dependency_failed')
    for name in case['case_checks']:
        path = 'responses/' + case['id']
        if name == 'same_session':
            observed = [r.get('session_id') for r in all_rows]
            initial = raw.get('initial') or {}
            value = None if not all(observed) or not initial.get('session') else (
                len(set(observed)) == 1 and observed[0] == raw.get('session_id')
                and not initial.get('turns'))
            add(name, value, path + '/initial')
        elif name == 'no_holdings_mutation':
            before, after = raw.get('before'), raw.get('after')
            add(name, None if before is None or after is None else before == after, path + '/before,after')
        elif name == 'owner_isolation':
            value = True
            for row in all_rows:
                after = row.get('after') or {}
                runs = after.get('runs', [])
                if any(r.get('user_id') != uid for r in runs) or any(not isinstance(p, str) or p not in owned for p in portfolio_ids(structured(row))):
                    value = False
                    break
                if after.get('session') and after['session'].get('user_id') != uid:
                    value = False
                    break
                if not after.get('session'):
                    value = None
                if row.get('run_ids') and (not runs or not portfolio_ids(structured(row)) and any(
                    r.get('workflow') == 'PortfolioDiagnosis' for r in runs)):
                    value = None
            add(name, value, path + '/turns/*/after', 'Observed owner and portfolio scope; not a general authorization penetration test')
        elif name == 'trace_complete':
            complete = all(r.get('status') == 'completed' and r.get('events') and r.get('after', {}).get('turns')
                           and len(r['after']['turns']) == 1
                           and r['after']['turns'][0].get('request_id') == r.get('request_id')
                           and r['after']['turns'][0].get('chat_session_id') == r.get('session_id')
                           and r.get('after', {}).get('session')
                           and (not r.get('run_ids') or (
                               r.get('after', {}).get('runs') and
                               all(k in r.get('detail', {}) for k in ('run', 'nodes', 'toolCalls', 'blackboardJson', 'outputJson'))))
                           for r in all_rows)
            add(name, True if complete else None, path + '/turns', 'Missing evidence is unverifiable')
        else:
            add(name, None, path, 'No deterministic evidence adapter')

    for turn in case['turns']:
        expected = turn['expected']
        row = rows.get(turn['turn'], {})
        path = f"responses/{case['id']}/turns/{turn['turn']}"
        if row.get('status') != 'completed':
            add('execution', False, path, row.get('error', 'not_run'),
                'bug' if row.get('error') in ('run_failed', 'unsupported_approval', 'multiple_runs_for_turn') else 'dependency_failed')
            for name in expected['checks']:
                add(name, None, path, 'Turn did not complete', 'dependency_failed')
            continue
        runs = row.get('after', {}).get('runs', [])
        intent = INTENTS[expected['intent']]
        events = row.get('events', [])
        action = next((e.get('action') for e in events if e.get('type') == 'conversation_action'), None)
        if intent['creates_run']:
            actual = (row.get('detail') or {}).get('run', {}).get('workflowType')
            wanted = WORKFLOWS[intent['workflow']]
            workflow_value = None if actual is None or not row.get('after', {}).get('turns') else actual == wanted and len(runs) == 1
            add('workflow', workflow_value, path + '/detail/run',
                f'Expected {wanted}; observed {actual}')
            if expected.get('ticker') and intent['workflow'] == 'Research':
                # Run input is authoritative; absence cannot establish ticker correctness.
                inp = runs[0].get('input') if len(runs) == 1 else None
                ticker = obj(inp).get('ticker')
                add('research_ticker', None if ticker is None else ticker == expected['ticker'], path + '/after/runs/input')
            board = obj((row.get('detail') or {}).get('blackboardJson'))
            if expected['intent'] == 'insufficient_data':
                comparison = board.get('portfolioScenarioComparison') or {}
                observed = [s for s in [comparison.get('baseline'), *(comparison.get('scenarios') or [])] if s]
                value = (not any(s.get('metrics') is not None or s.get('status') == 'Calculated' for s in observed)
                         if observed else None)
                add('insufficient_history', value, path + '/detail/blackboardJson/portfolioScenarioComparison',
                    'A complete scenario risk estimate requires 120 common returns')
            if intent['workflow'] == 'Portfolio':
                gate = board.get('portfolioQualityGate')
                add('output_gate', None if gate is None else gate.get('passed') is True, path + '/detail/blackboardJson/portfolioQualityGate')
        else:
            wanted = {'clarify': {'AskClarification'}, 'reset': {'ResetContext'},
                      'list_portfolios': {'DirectResponse', 'AskClarification'},
                      'select_portfolio': {'DirectResponse', 'AskClarification'}}[expected['intent']]
            add('action', action in wanted and not runs and not row.get('run_ids'), path + '/events',
                'Observed conversation action and persisted run creation')
        for name in expected['checks']:
            value, reason = None, ''
            if CHECKS[name]['scorer'] == 'semantic':
                reason = 'Requires human review or a calibrated semantic judge'
            elif name == 'no_premature_execution':
                value = (not runs and not row.get('run_ids') and (expected['intent'] != 'clarify' or action == 'AskClarification')
                         if row.get('after', {}).get('turns') else None)
            elif name in ('resource_binding', 'selection_retained'):
                pid = entry['owner_a']['portfolios'].get(expected.get('portfolio_ref'))
                if pid and intent['creates_run']:
                    value = binding(row, pid)
                elif pid:
                    following = [rows.get(t['turn'], {}) for t in case['turns']
                                 if int(t['turn'][1:]) > int(turn['turn'][1:])
                                 and t['expected'].get('portfolio_ref') == expected.get('portfolio_ref')
                                 and INTENTS[t['expected']['intent']]['creates_run']]
                    value = binding(following[0], pid) if following else None
                    reason = 'Verified against the next dependent portfolio run'
            elif name == 'computed_comparison':
                try:
                    value, reason = comparison_check(row, frozen['portfolios'][expected['portfolio_ref']], expected.get('constraints'))
                except (KeyError, TypeError, ValueError, ZeroDivisionError):
                    value, reason = None, 'Malformed or incomplete comparison evidence'
            elif name == 'research_grounded':
                try:
                    value, reason = research_check(row, expected.get('research_case_refs', []), research)
                except (KeyError, TypeError, ValueError):
                    value, reason = None, 'Malformed or incomplete research evidence'
            elif name == 'reset_boundary':
                # Context emptiness alone cannot prove history was excluded from the router.
                if expected['intent'] == 'reset' and action != 'ResetContext':
                    value = False
                reason = 'Router history/evidence input is not persisted; reset action alone is insufficient'
            elif name == 'owned_portfolio_list':
                reason = 'Response has no structured list of emitted portfolio IDs'
            else:
                reason = 'Required evidence unavailable'
            add(name, value, path, reason, 'capability_gap' if name in CAPABILITY_GAPS else 'bug')
    statuses = [c['status'] for c in checks]
    failed = 'fail' in statuses
    completed = not failed and 'unverifiable' not in statuses
    return dict(case_id=case['id'], review_status=case['review_status'], checks=checks,
                status='fail' if failed else 'pass' if completed else 'unverifiable',
                conversation_success=(False if failed else True if completed else None)
                if case['review_status'] == 'verified' else None)


def summarize(results):
    checks = [c for r in results for c in r['checks']]
    deterministic = [c for c in checks if CHECKS.get(c['check'], {}).get('scorer') != 'semantic']
    decisive = [c for c in deterministic if c['status'] != 'unverifiable']
    # Keep unresolved verified cases in the denominator; never inflate success by excluding them.
    eligible = [r for r in results if r['review_status'] == 'verified']
    classified = {kind: sum(c['status'] == 'fail' and c['failure_class'] == kind for c in checks)
                  for kind in ('bug', 'capability_gap', 'dependency_failed')}
    return dict(suite='conversation_journey_v1', scorer_version=VERSION, results=results,
                attempted=len(results), failed=sum(r['status'] == 'fail' for r in results),
                deterministic_pass_rate=sum(c['status'] == 'pass' for c in decisive)/len(decisive) if decisive else None,
                deterministic_coverage=len(decisive)/len(deterministic) if deterministic else None,
                conversation_success_rate=(sum(r['conversation_success'] is True for r in eligible)/len(eligible)
                                           if eligible and all(r['status'] != 'unverifiable' for r in eligible) else None),
                conversation_denominator=len(eligible), failure_counts=classified,
                unresolved_cases=sum(r['status'] == 'unverifiable' for r in results),
                cost_usd=None, cost_scope='Unknown total cost; no judge calls; API/provider billing is external')
