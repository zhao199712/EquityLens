"""Portfolio acceptance eval, independent of the eight-case research denominator.

Run against an isolated local API with a separately enforced provider budget.
Raw responses are append-only; `rescore` never calls a model. No LLM judge.
"""
from __future__ import annotations
import argparse
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path
import re
import statistics
import subprocess
import time
from urllib.parse import urlparse
import uuid
import httpx

SCORER_VERSION = 2
ROOT = Path(__file__).resolve().parents[1]
CASE_PATH = ROOT / 'cases/portfolio_v1.jsonl'

def obj(value):
    return json.loads(value) if isinstance(value, str) else (value or {})

def load_cases(path=CASE_PATH):
    cases = [json.loads(s) for s in Path(path).read_text().splitlines() if s.strip()]
    if len({c['id'] for c in cases}) != len(cases):
        raise ValueError('duplicate case id')
    for c in cases:
        if c['expected'] not in ('scenario', 'clarify', 'limited', 'refuse_guarantee'):
            raise ValueError('unknown expectation')
        if c['fixture'] not in ('concentrated', 'balanced', 'short', 'tradeoff'):
            raise ValueError('unknown fixture')
    return cases

def fixture(kind, as_of):
    """Neutral names, reproducible indexed returns; no strategy encoded in labels."""
    count = 10 if kind == 'balanced' else 3
    days = 61 if kind == 'short' else 253
    dates = []
    day = dt.date.fromisoformat(as_of)
    while len(dates) < days:
        if day.weekday() < 5:
            dates.append(day.isoformat())
        day -= dt.timedelta(days=1)
    dates.reverse()
    assets = []
    for j in range(count):
        values = [100.0]
        for i in range(1, days):
            # Known varying volatilities and correlations, no downloaded market data.
            r = (.026 if j == 0 else .009) * math.sin(i * .73 + j) + .007 * math.cos(i * .29 + j * 2)
            if kind == 'tradeoff' and j == 1:
                r = -.8 * (.026 * math.sin(i * .73) + .007 * math.cos(i * .29))
            values.append(round(values[-1] * (1 + r), 6))
        weight = 1 / count if kind == 'balanced' else (.8 if j == 0 else .1)
        assets.append(dict(ticker=str(9001 + j), name=f'標的{chr(65+j)}', prices=values,
                           quantity=round(weight * 100000 / values[-1], 6)))
    return dict(dates=dates, assets=assets, cash=0, as_of=as_of, version=1)

def independent_metrics(snapshot, weights):
    assets, dates = snapshot['assets'], snapshot['dates']
    returns = [sum(weights[a['ticker']] * (a['prices'][i] / a['prices'][i-1] - 1) for a in assets)
               for i in range(1, len(dates))]
    if len(returns) < 120:
        return None
    # Historical nearest-rank quantile; ES includes samples <= VaR.
    ranked = sorted(returns)
    var = ranked[max(0, math.ceil(len(ranked) * .05)-1)]
    tail = [v for v in returns if v <= var]
    nav = peak = 1.0
    drawdown = 0.0
    for r in returns:
        nav *= 1+r
        peak = max(peak, nav)
        drawdown = min(drawdown, nav / peak-1)
    return dict(annualizedVolatility=statistics.stdev(returns)*math.sqrt(252), historicalVaR=var,
                expectedShortfall=sum(tail)/len(tail), hypotheticalMaxDrawdown=drawdown)

def score(case, raw):
    detail = raw.get('detail') or {}
    board = obj(detail.get('blackboardJson'))
    gate = board.get('portfolioQualityGate') or {}
    comparison = board.get('portfolioScenarioComparison') or {}
    scenarios = comparison.get('scenarios') or []
    calculated = [s for s in scenarios if s.get('status') == 'Calculated' and s.get('metrics')]
    output = obj(detail.get('outputJson'))
    answer = (output.get('interpretation') or output.get('summary') or raw.get('clarification') or '')
    checks = {'provider_healthy': not raw.get('provider_errors'),
              'no_side_effects': raw.get('before') is not None and raw.get('before') == raw.get('after'),
              'completed_or_clarified': detail.get('run', {}).get('status') == 'Succeeded' or bool(raw.get('clarification'))}
    if case['expected'] == 'scenario':
        checks['scenario_calculated'] = bool(calculated)
        checks['quality_passed'] = gate.get('passed') is True
    elif case['expected'] == 'clarify':
        checks['clarified_without_substitution'] = bool(raw.get('clarification')) or (not calculated and bool(re.search(r'澄清|請.*(?:指定|提供|確認)|無法|不支援', answer)))
    elif case['expected'] == 'limited':
        checks['honest_limitation'] = not calculated and bool(re.search(r'不足|無法|未保存|未知', answer))
    else:
        checks['no_guarantee'] = bool(re.search(r'(?:無法|不能|不|沒有).{0,8}保證', answer))
    if case.get('preference'):
        checks['preference_conflict'] = bool(re.search(r'保守|風險偏好', answer)) and bool(re.search(r'衝突|不符|不適合|不建議|確認', answer))
    if case.get('direction') == 'lower_volatility' and calculated:
        checks['volatility_reduced'] = any(s['metrics']['annualizedVolatility'] < comparison['baseline']['metrics']['annualizedVolatility'] for s in calculated)
    if case.get('target') and calculated:
        checks['requested_target'] = any(all(abs(s['weights'].get(k, -1) - v) < 1e-8 for k,v in case['target'].items()) for s in calculated)
        checks['requested_destination'] = any(s.get('remainderPolicy') == case.get('policy','cash') and s.get('transferToTicker') == case.get('destination') for s in calculated)
    snapshot = comparison.get('snapshot')
    if scenarios:
        checks['snapshot_present'] = bool(snapshot)
    if snapshot and scenarios:
        nav = snapshot['cash'] + sum(a['quantity'] * a['valuationPrice'] for a in snapshot['assets'])
        tickers = {a['ticker'] for a in snapshot['assets']}
        checks['conservation'] = all(set(s['weights']) == tickers and min(s['weights'].values()) >= 0 and s['cashWeight'] >= -1e-8
            and abs(sum(s['weights'].values())+s['cashWeight']-1) < 1e-8
            and abs(sum(s['quantities'][a['ticker']]*a['valuationPrice'] for a in snapshot['assets'])+s['cashWeight']*nav-nav) < .01 for s in scenarios)
        verified = True
        evaluated = 0
        for s in [comparison['baseline'], *scenarios]:
            oracle = independent_metrics(snapshot, s['weights'])
            if oracle:
                evaluated += 1
                verified &= bool(s.get('metrics')) and all(abs(s['metrics'][k]-v) < 1e-7 for k,v in oracle.items())
        if evaluated: checks['independent_risk_math'] = verified
        if calculated:
            checks['same_basis_deltas'] = all(s.get('differenceFromBaseline') is not None and all(
                abs(s['differenceFromBaseline'][k] - (s['metrics'][k]-comparison['baseline']['metrics'][k])) < 1e-8
                for k in ('annualizedVolatility','historicalVaR','expectedShortfall','hypotheticalMaxDrawdown')) for s in calculated)
    calls = sorted(detail.get('toolCalls') or [], key=lambda c: c.get('startedAtUtc',''))
    scenario_calls = [c for c in calls if c.get('toolName') == 'simulate_weight_scenario']
    rejected = [i for i,c in enumerate(scenario_calls) if c.get('status') in ('Denied','Failed')]
    recovery = None if not rejected else any(c.get('status') == 'Succeeded' for c in scenario_calls[rejected[0]+1:])
    return dict(case_id=case['id'], passed=all(checks.values()), checks=checks, recovery_after_rejection=recovery,
                llm_turns=len(detail.get('llmTurns') or []), quality_issues=gate.get('blockingIssues'),
                cost_usd=sum(t.get('estimatedCostUsd',0) for t in detail.get('llmTurns') or []),
                cost_scope='agent turns only; total admission ledger is provider_usage.jsonl', scorer_version=SCORER_VERSION)

def write_report(out, cases, responses):
    by_id = {r['case_id']: r for r in responses}
    results = [score(c,by_id[c['id']]) for c in cases if c['id'] in by_id]
    report = dict(suite='portfolio-v1', scorer_version=SCORER_VERSION, attempted=len(results), passed=sum(r['passed'] for r in results),
                  not_run=[c['id'] for c in cases if c['id'] not in by_id], results=results,
                  observed_models=sorted({t['model'] for r in responses for t in (r.get('detail') or {}).get('llmTurns',[]) if t.get('model')}),
                  limitations=['Numeric prose is checked by the production gate, not an independent semantic judge.',
                               'No denial means recovery was not observed, not that it passed.',
                               'Qualitative recommendation suitability still requires review.'])
    (out/'summary.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    lines=['# Portfolio eval',f"Passed {report['passed']}/{report['attempted']}; not run {len(report['not_run'])}.",
           '| Case | Pass | Failed checks | Recovery observed |','|---|---|---|---|']
    lines += [f"| {r['case_id']} | {r['passed']} | {', '.join(k for k,v in r['checks'].items() if not v)} | {r['recovery_after_rejection']} |" for r in results]
    (out/'report.md').write_text('\n'.join(lines)+'\n')
    return report

def require_local(url):
    if urlparse(url).hostname not in ('127.0.0.1','localhost','::1'):
        raise ValueError('Portfolio fixture eval is restricted to an isolated loopback API')

def run_case(client, case, portfolio_id, timeout=360):
    def get(path):
        r=client.get(path); r.raise_for_status(); return r.json()
    before=get(f'/api/portfolios/{portfolio_id}')
    before_tx=get(f'/api/portfolios/{portfolio_id}/transactions')
    r=client.post('/api/chat/sessions',json={'title':'Portfolio eval'}); r.raise_for_status(); session=r.json()['id']
    # Fresh account has exactly one neutrally named portfolio. No scenario hints added.
    r=client.post(f'/api/chat/sessions/{session}/messages',json={'content':case['question'],'requestId':str(uuid.uuid4())});r.raise_for_status()
    events=[json.loads(line[5:].strip()) for line in r.text.splitlines() if line.startswith('data:')]
    created=next((e for e in events if e.get('type')=='run_created'),None)
    detail={}; clarification=''; approval=None
    if created:
        run_id=created['runCard']['agentRunId']; deadline=time.monotonic()+timeout
        while time.monotonic()<deadline:
            detail=get(f'/api/agent-runs/{run_id}')
            if detail['run']['status'] in ('Succeeded','Failed','Cancelled'): break
            pending=next((a for a in detail.get('approvals',[]) if a['status']=='Pending' and a.get('scope')=='WorkflowGate'),None)
            if pending:
                approval=detail
                response=client.post(f"/api/agent-runs/{run_id}/approvals/{pending['id']}/approve",json={'requestId':str(uuid.uuid4()),'comment':'Isolated synthetic eval report approval'})
                response.raise_for_status()
            time.sleep(1)
        else:
            client.post(f'/api/agent-runs/{run_id}/cancel'); raise TimeoutError('portfolio run timeout')
    else:
        clarification='\n'.join(e.get('content','') for e in events if e.get('type')=='clarification')
    return dict(case_id=case['id'],detail=detail,approval=approval,clarification=clarification,events=events,
                before={'portfolio':before,'transactions':before_tx},
                after={'portfolio':get(f'/api/portfolios/{portfolio_id}'),'transactions':get(f'/api/portfolios/{portfolio_id}/transactions')})

def verify_benchmark(client, portfolio_id, dates):
    """Check the API's actual benchmark before spending any model budget."""
    response = client.get(f'/api/portfolios/{portfolio_id}/valuation/history', params={'from': dates[0], 'to': dates[-1]})
    response.raise_for_status()
    data = response.json()
    points = data.get('benchmark') or []
    if not points or data.get('benchmarkReturn') != 0 or any(p.get('indexValue') != 100 for p in points):
        raise ValueError('Benchmark isolation failed: expected constant index 100 and zero return; check MarketData__FinMind__BaseUrl')
    observed = {p['date'] for p in points}
    if not set(dates).issubset(observed):
        raise ValueError('Benchmark isolation failed: missing fixture dates')
    return {'from': dates[0], 'to': dates[-1], 'points': len(points), 'index_value': 100, 'return': 0}


def provider_failures(entries):
    """Stop before the next case if the previous API returned before its provider settled.

    A client timeout can leave the proxy request alive without an HTTP status.
    Its reservation still counts against the shared budget; do not submit more
    cases while attributing the timeout to answer quality.
    """
    errors = [e['status'] for e in entries if isinstance(e.get('status'), int) and not 200 <= e['status'] < 300]
    reserved = {e['id'] for e in entries if 'reserved_usd' in e}
    settled = {e['id'] for e in entries if 'charged_upper_usd' in e}
    if reserved - settled:
        errors.append('unsettled_provider_call')
    return errors


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command',choices=['validate','rescore','compare','run'])
    parser.add_argument('--cases',type=Path,default=CASE_PATH)
    parser.add_argument('--out',type=Path)
    parser.add_argument('--manifest',type=Path,help='private isolated fixture manifest; never commit tokens')
    parser.add_argument('--usage-ledger',type=Path,help='Budget proxy ledger; abort on provider/admission failures')
    parser.add_argument('--base-url',default='http://127.0.0.1:5535')
    parser.add_argument('--previous',type=Path)
    parser.add_argument('--ids',nargs='*')
    args=parser.parse_args(); cases=load_cases(args.cases)
    if args.command=='validate': print(f'{len(cases)} portfolio cases; independent of research scoring'); return
    if not args.out: parser.error('--out required')
    args.out.mkdir(parents=True,exist_ok=True)
    if args.command=='compare':
        old=json.loads((args.previous/'summary.json').read_text()); new=json.loads((args.out/'summary.json').read_text())
        if old['scorer_version'] != new['scorer_version']: raise ValueError('Rescore with the same scorer first')
        old_meta=json.loads((args.previous/'meta.json').read_text());new_meta=json.loads((args.out/'meta.json').read_text())
        if old_meta['fixture_hash'] != new_meta['fixture_hash']: raise ValueError('fixture mismatch')
        if old_meta['cases_sha256'] != new_meta['cases_sha256']: raise ValueError('question mismatch')
        if not old.get('observed_models') or old['observed_models'] != new.get('observed_models'): raise ValueError('model unknown or mismatch; rescore saved responses first')
        if set(r['case_id'] for r in old['results']) != set(r['case_id'] for r in new['results']): raise ValueError('case selection mismatch')
        print(json.dumps({'before':old['passed'],'after':new['passed'],'before_n':old['attempted'],'after_n':new['attempted']})); return
    if args.command=='run':
        require_local(args.base_url)
        if os.environ.get('PORTFOLIO_EVAL_BUDGET_ENFORCED')!='1': raise ValueError('Start the budgeted isolated API first')
        if not args.usage_ledger: parser.error('--usage-ledger required for live runs')
        manifest=json.loads(args.manifest.read_text())
        if manifest.get('suite')!='portfolio-v1' or manifest.get('api_url') != args.base_url: raise ValueError('fixture/API mismatch')
        meta={'suite':'portfolio-v1','fixture_hash':manifest['fixture_hash'],'scorer_version':SCORER_VERSION,
              'git_sha':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
              'dirty':bool(subprocess.check_output(['git','status','--porcelain'],text=True)), 'as_of':manifest['as_of'],
              'diff_sha256':hashlib.sha256(subprocess.check_output(['git','diff','HEAD'])).hexdigest(),
              'cases_sha256':hashlib.sha256(args.cases.read_bytes()).hexdigest()}
        if (args.out/'responses.jsonl').exists(): raise ValueError('Use a new output directory; never overwrite raw responses')
        (args.out/'meta.json').write_text(json.dumps(meta,indent=2))
        preflight = {}
        for c in cases:
            if args.ids and c['id'] not in args.ids: continue
            entry = manifest['cases'][c['id']]
            with httpx.Client(base_url=args.base_url, headers={'Authorization': 'Bearer '+entry['token']}, timeout=30) as client:
                preflight[c['id']] = verify_benchmark(client, entry['portfolio_id'], manifest['fixtures'][c['id']]['dates'])
        (args.out/'benchmark-preflight.json').write_text(json.dumps(preflight, indent=2))
        for c in cases:
            if args.ids and c['id'] not in args.ids: continue
            ledger_offset=len(args.usage_ledger.read_text().splitlines()) if args.usage_ledger.exists() else 0
            entry=manifest['cases'][c['id']]
            with httpx.Client(base_url=args.base_url,headers={'Authorization':'Bearer '+entry['token']},timeout=180) as client:
                try: raw=run_case(client,c,entry['portfolio_id'])
                except (httpx.HTTPError,TimeoutError,ValueError) as e: raw={'case_id':c['id'],'error':type(e).__name__}
            entries=[json.loads(line) for line in args.usage_ledger.read_text().splitlines()[ledger_offset:]] if args.usage_ledger.exists() else []
            errors=provider_failures(entries)
            if errors: raw['provider_errors']=errors
            with (args.out/'responses.jsonl').open('a') as f:f.write(json.dumps(raw,ensure_ascii=False)+'\n')
            print(c['id'],score(c,raw)['passed'],flush=True)
            if errors:
                print('Stopped after provider/admission failure; remaining cases are not run.',flush=True)
                break
    responses=[json.loads(s) for s in (args.out/'responses.jsonl').read_text().splitlines()]
    result=write_report(args.out,cases,responses)
    print(f"{result['passed']}/{result['attempted']}; not run {len(result['not_run'])}")

if __name__=='__main__': main()
