import pytest
from equitylens_eval.router_eval import evaluate, read_jsonl, contains
from pathlib import Path


def cases():
    return [dict(id='one', message='test', category='research', expected=dict(action='route', workflow='Research', params={'ticker':'2330'})),
            dict(id='two', message='unknown', category='ambiguous', expected=dict(action='clarify', workflow=None, params={}))]


def test_no_observations_is_unknown_not_fake_success():
    result = evaluate(cases(), [])
    assert result['status'] == 'not_run'
    assert result['accuracy'] is None
    assert result['action_confusion_matrix'] == {}


def test_missing_and_wrong_key_parameters_count_as_failures():
    result = evaluate(cases(), [dict(id='one', action='route', workflow='Research', params={'ticker':'2317'})])
    assert result['accuracy'] == 0
    assert result['action_accuracy'] == .5
    assert result['missing_count'] == 1
    assert result['status'] == 'partial'


def test_confusion_matrix_is_derived_from_actual_decisions():
    result = evaluate(cases(), [dict(id='one', action='route', workflow='Research', params={'ticker':'2330'}),
                              dict(id='two', action='route', workflow='Portfolio', params={})])
    assert result['accuracy'] == .5
    assert result['action_confusion_matrix'] == {'route':{'route':1}, 'clarify':{'route':1}}
    assert result['workflow_confusion_matrix']['None']['Portfolio'] == 1


def test_dataset_covers_required_categories_and_rejects_unknown_predictions():
    dataset = read_jsonl(Path(__file__).parents[1]/'cases/router_v1.jsonl')
    assert 20 <= len(dataset) <= 30
    assert {'research','portfolio','mixed','ambiguous','unauthorized','no_portfolio','unsupported'} <= {c['category'] for c in dataset}
    evaluate(dataset, [])
    with pytest.raises(ValueError, match='unknown'):
        evaluate(cases(), [dict(id='bad')])
    with pytest.raises(ValueError, match='Duplicate'):
        evaluate(cases(), [dict(id='one'),dict(id='one')])


def test_parameters_preserve_scenario_destination_and_types():
    expected={'scenarios':[{'targetWeights':{'2327':.1},'remainderPolicy':'holding','transferToTicker':'2330'}]}
    assert contains(expected, expected)
    assert not contains({'scenarios':[{'targetWeights':{'2327':.1},'remainderPolicy':'cash'}]},expected)
    assert not contains({'weight':True},{'weight':1})


def test_dataset_covers_index_etf_destination_and_prompt_injection():
    dataset = {c['id']: c for c in read_jsonl(Path(__file__).parents[1]/'cases/router_v1.jsonl')}
    etf = dataset['router-27']['expected']
    assert etf['action'] == 'route' and etf['workflow'] == 'Portfolio'
    assert etf['params']['scenarios'][0]['remainderPolicy'] == 'index_etf'
    assert etf['params']['scenarios'][0]['transferToTicker'] == '0050'
    injection = dataset['router-28']
    assert injection['category'] == 'prompt_injection'
    assert injection['expected'] == {'action': 'reject', 'workflow': None, 'params': {}}
