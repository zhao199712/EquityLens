import copy
import pytest
from equitylens_eval.portfolio import fixture, load_cases, score, independent_metrics, require_local


def test_cases_separate_and_unprompted():
    cases=load_cases()
    assert len(cases)==14
    assert cases[0]['question']=='可以給我優化建議嗎？'
    assert cases[0]['fixture']=='concentrated'
    assert all('verified' not in c for c in cases)  # not research score denominator


def test_fixture_deterministic_neutral_and_insufficient():
    a=fixture('concentrated','2026-11-03')
    assert a==fixture('concentrated','2026-11-03')
    assert a['assets'][0]['name']=='標的A'
    assert len(a['dates'])==253
    assert len(fixture('short','2026-11-03')['dates'])==61


def successful():
    f=fixture('concentrated','2026-11-03')
    snapshot={**f,'assets':[{**a,'valuationPrice':a['prices'][-1]} for a in f['assets']]}
    nav=sum(a['quantity']*a['valuationPrice'] for a in snapshot['assets'])
    base={a['ticker']:a['quantity']*a['valuationPrice']/nav for a in snapshot['assets']}
    weights={**base,'9001':.3}
    baseline=independent_metrics(snapshot,base);metrics=independent_metrics(snapshot,weights)
    s={'status':'Calculated','weights':weights,'cashWeight':1-sum(weights.values()),'metrics':metrics,
       'quantities':{a['ticker']:weights[a['ticker']]*nav/a['valuationPrice'] for a in snapshot['assets']},
       'differenceFromBaseline':{k:metrics[k]-baseline[k] for k in metrics}}
    detail={'run':{'status':'Succeeded'},'blackboardJson':{'portfolioQualityGate':{'passed':True},
        'portfolioScenarioComparison':{'snapshot':snapshot,'baseline':{'weights':base,'metrics':baseline},'scenarios':[s]}},'toolCalls':[]}
    return {'before':{'holdings':[1]},'after':{'holdings':[1]},'detail':detail}


def test_math_conservation_and_no_side_effects():
    raw=successful();case=load_cases()[0]
    assert score(case,raw)['passed']
    assert score(case,raw)['recovery_after_rejection'] is None
    for mutate in ('cash','math','holdings','delta'):
        changed=copy.deepcopy(raw);s=changed['detail']['blackboardJson']['portfolioScenarioComparison']['scenarios'][0]
        if mutate=='cash':s['cashWeight']+=.1
        if mutate=='math':s['metrics']['annualizedVolatility']+=.01
        if mutate=='delta':s['differenceFromBaseline']['annualizedVolatility']+=.01
        if mutate=='holdings':changed['after']['holdings']=[2]
        assert not score(case,changed)['passed'],mutate
    short=copy.deepcopy(raw)
    comparison=short['detail']['blackboardJson']['portfolioScenarioComparison']
    comparison['snapshot']['dates']=comparison['snapshot']['dates'][:61]
    comparison['scenarios'][0]['status']='InsufficientHistory'
    checks=score(case,short)['checks']
    assert 'independent_risk_math' not in checks
    assert 'same_basis_deltas' not in checks


def test_failure_is_not_zero_violation_success():
    raw=successful()
    raw['detail']['toolCalls']=[{'toolName':'simulate_weight_scenario','status':'Denied','startedAtUtc':'1'}]
    assert score(load_cases()[0],raw)['recovery_after_rejection'] is False
    raw['detail']['toolCalls'].append({'toolName':'simulate_weight_scenario','status':'Succeeded','startedAtUtc':'2'})
    assert score(load_cases()[0],raw)['recovery_after_rejection'] is True
    assert not score(load_cases()[0],{})['passed']
    failed_provider=successful()
    failed_provider['provider_errors']=[503]
    assert not score(load_cases()[0],failed_provider)['passed']


def test_production_rejected():
    with pytest.raises(ValueError):require_local('https://equitylens.pages.dev')
    require_local('http://127.0.0.1:5535')


def test_budget_reserves_concurrent_calls_and_charges_unknown_outcomes(tmp_path):
    from equitylens_eval.budget_proxy import Budget
    budget=Budget(.003,tmp_path/'ledger.jsonl')
    payload={'model':'deepseek-v4-flash','max_tokens':1000,'messages':[{'content':'test'}]}
    first=budget.reserve(payload)
    with pytest.raises(ValueError):budget.reserve(payload)
    budget.finish(first,{},502)
    assert budget.spent > .0012
    with pytest.raises(ValueError):budget.reserve(payload)
    with pytest.raises(ValueError):Budget(1,tmp_path/'ledger.jsonl')
    with pytest.raises(ValueError):budget.reserve({**payload,'model':'unpriced'})


def test_budget_accounts_for_tool_schema_and_null_usage(tmp_path):
    from equitylens_eval.budget_proxy import Budget
    budget=Budget(.01,tmp_path/'ledger.jsonl')
    payload={'model':'deepseek-v4-flash','max_tokens':100,'messages':[]}
    with pytest.raises(ValueError):budget.reserve({**payload,'tools':[{'description':'x'*100000}]})
    key=budget.reserve(payload)
    reserved=budget.pending[key]
    budget.finish(key,None,502)
    assert budget.spent==reserved


def test_unsettled_provider_call_stops_next_case_without_refunding_reservation():
    from equitylens_eval.portfolio import provider_failures
    pending = [{'id': 'a', 'reserved_usd': .02}]
    assert provider_failures(pending) == ['unsettled_provider_call']
    assert provider_failures(pending + [{'id': 'a', 'charged_upper_usd': .01, 'status': 200}]) == []
    assert provider_failures(pending + [{'id': 'a', 'charged_upper_usd': .02, 'status': 503}]) == [503]
    assert provider_failures([{'status': 402, 'admission_refused': True}]) == [402]
    assert pending[0]['reserved_usd'] == .02


def test_benchmark_preflight_rejects_external_or_incomplete_data_before_ai():
    import httpx
    from equitylens_eval.portfolio import verify_benchmark
    dates = ['2026-01-01', '2026-01-02']
    valid = {'benchmarkReturn': 0, 'benchmark': [{'date': d, 'indexValue': 100} for d in dates]}
    for payload, expected in [(valid, True), ({**valid, 'benchmarkReturn': .55}, False),
                              ({**valid, 'benchmark': valid['benchmark'][:1]}, False),
                              ({**valid, 'benchmark': []}, False),
                              ({**valid, 'benchmark': [{'date': d, 'indexValue': 101} for d in dates]}, False)]:
        def handle(request):
            assert request.method == 'GET'
            assert request.url.path == '/api/portfolios/test/valuation/history'
            return httpx.Response(200, json=payload)
        with httpx.Client(base_url='http://127.0.0.1', transport=httpx.MockTransport(handle)) as client:
            if expected: assert verify_benchmark(client, 'test', dates)['return'] == 0
            else:
                with pytest.raises(ValueError, match='Benchmark isolation failed'): verify_benchmark(client, 'test', dates)
