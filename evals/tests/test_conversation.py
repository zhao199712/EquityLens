import copy

import pytest

from equitylens_eval import conversation as conv


@pytest.fixture(scope='module')
def suite():
    return (conv.read_jsonl(conv.CASE_PATH), conv.load_fixture(), conv._research_index(conv.RESEARCH_PATH))


def _case(cases, case_id):
    return next(case for case in cases if case['id'] == case_id)


def _expect_invalid(suite, mutate, message):
    cases, fixture, research = copy.deepcopy(suite)
    mutate(cases, fixture)
    with pytest.raises(ValueError, match=message):
        conv.validate_cases(cases, fixture, research)


def test_dataset_is_valid_and_all_draft(suite):
    cases, fixture, research = suite
    conv.validate_cases(cases, fixture, research)
    summary = conv.summarize(cases)
    assert summary['cases'] == 11
    assert summary['draft_cases'] == 11
    assert summary['deterministic_checks'] > 0 and summary['semantic_checks'] > 0


def test_journey_00_reproduces_the_reported_memory_bug_without_research(suite):
    case = _case(suite[0], 'journey-00')
    intents = [turn['expected']['intent'] for turn in case['turns']]
    assert intents == ['list_portfolios', 'select_portfolio', 'portfolio_diagnosis']
    assert case['turns'][2]['user'] == '這個波動率如何？'
    assert '組合甲' not in case['turns'][2]['user']
    assert case['turns'][2]['expected']['portfolio_ref'] == 'alpha'
    assert 'selection_retained' in case['turns'][2]['expected']['checks']
    assert case['research_case_refs'] == []


def test_ambiguous_pronoun_was_removed_from_journey_03(suite):
    last = _case(suite[0], 'journey-03')['turns'][-1]
    assert '它' not in last['user']
    assert '這個組合' in last['user']


def test_every_check_declares_a_scorer_and_capability_gaps_are_semantic():
    assert all(meta['scorer'] in {conv.DETERMINISTIC, conv.SEMANTIC} for meta in conv.CHECKS.values())
    for check in conv.CAPABILITY_GAPS:
        assert conv.CHECKS[check]['scorer'] == conv.SEMANTIC


def test_routing_memory_checks_are_deterministic():
    for check in ('selection_retained', 'resource_binding', 'reset_boundary', 'owner_isolation', 'no_premature_execution'):
        assert conv.CHECKS[check]['scorer'] == conv.DETERMINISTIC


def test_fixture_uses_same_name_decoys_and_no_prefilled_goldens(suite):
    fixture = suite[1]
    assert set(fixture['owners']['owner_a']['portfolios'].values()) == set(fixture['owners']['owner_b']['portfolios'].values())
    assert fixture['numeric_golden'] is None
    assert '2330' not in fixture['profiles']['no_direct']['weights']['alpha']


def test_rejects_evidence_that_crosses_a_reset(suite):
    def mutate(cases, _):
        _case(cases, 'journey-10')['turns'][5]['expected']['evidence_from_turns'] = ['t3']
    _expect_invalid(suite, mutate, 'reset boundary')


def test_rejects_forward_evidence_reference(suite):
    def mutate(cases, _):
        _case(cases, 'journey-01')['turns'][1]['expected']['evidence_from_turns'] = ['t3']
    _expect_invalid(suite, mutate, 'earlier turn')


def test_rejects_excluded_or_unverified_research_refs(suite):
    def mutate(cases, _):
        turn = _case(cases, 'journey-01')['turns'][0]['expected']
        turn['research_case_refs'] = ['2454-revenue-2025']
        turn['ticker'] = '2454'
        _case(cases, 'journey-01')['research_case_refs'] = ['2454-revenue-2025']
    _expect_invalid(suite, mutate, 'verified and not excluded')


def test_rejects_research_ref_for_another_ticker(suite):
    def mutate(cases, _):
        _case(cases, 'journey-05')['turns'][2]['expected']['research_case_refs'] = ['2330-eps-2025']
    _expect_invalid(suite, mutate, 'different ticker')


def test_rejects_holding_transfer_to_unheld_security(suite):
    def mutate(cases, _):
        _case(cases, 'journey-07')['turns'][3]['expected']['constraints']['transfer_to'] = '2454'
    _expect_invalid(suite, mutate, 'another held security')


def test_rejects_cash_mode_with_transfer_target(suite):
    def mutate(cases, _):
        _case(cases, 'journey-06')['turns'][3]['expected']['constraints']['transfer_to'] = '0050'
    _expect_invalid(suite, mutate, 'must not name a transfer target')


def test_rejects_target_weight_that_increases_the_source(suite):
    def mutate(cases, _):
        _case(cases, 'journey-06')['turns'][3]['expected']['constraints']['target_weight'] = 0.9
    _expect_invalid(suite, mutate, 'below the current weight')


def test_rejects_run_parameters_on_turns_that_create_no_run(suite):
    def mutate(cases, _):
        _case(cases, 'journey-02')['turns'][1]['expected']['ticker'] = '2330'
    _expect_invalid(suite, mutate, 'cannot carry run parameters')


def test_rejects_unknown_portfolio_alias_and_decoys(suite):
    def mutate(cases, _):
        _case(cases, 'journey-04')['turns'][3]['expected']['portfolio_ref'] = 'decoy_beta'
    _expect_invalid(suite, mutate, 'not an owner_a portfolio')


def test_rejects_unknown_check_and_bad_turn_order(suite):
    _expect_invalid(suite, lambda cases, _: _case(cases, 'journey-01')['turns'][0]['expected']['checks'].append('vibes'), 'unknown check')
    _expect_invalid(suite, lambda cases, _: _case(cases, 'journey-01')['turns'][1].update(turn='t5'), 'numbered')


def test_rejects_turn_count_mismatch_and_missing_required_fields(suite):
    _expect_invalid(suite, lambda cases, _: _case(cases, 'journey-01').update(max_user_turns=2), 'max_user_turns')
    _expect_invalid(suite, lambda cases, _: _case(cases, 'journey-00')['turns'][2]['expected'].pop('portfolio_ref'), 'requires portfolio_ref')


def test_contract_review_is_separate_from_seed_numeric_verification(suite):
    cases, fixture, research = copy.deepcopy(suite)
    _case(cases, 'journey-01')['review_status'] = 'verified'
    conv.validate_cases(cases, fixture, research)
    _expect_invalid(suite, lambda _, fixture: fixture.update(numeric_golden={'vol': 0.2}), 'must not be prefilled')


def test_rejects_decoys_without_shared_names(suite):
    _expect_invalid(suite, lambda _, fixture: fixture['owners']['owner_b']['portfolios'].update(decoy_alpha='別的名字'), 'reuse owner_a portfolio names')


def test_cli_validate_prints_summary(capsys):
    assert conv.main(['validate']) == 0
    assert '"cases": 11' in capsys.readouterr().out
