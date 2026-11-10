"""Multi-turn conversation journey suite: case contract and validation.

Validation and CLI entry point; runtime commands load their dependencies lazily.
Expected values are for scorers only and must never be sent to the model.
"""
from __future__ import annotations

import argparse
import json
import re
import math
import sys
from pathlib import Path
from typing import Any

CASES_DIR = Path(__file__).resolve().parent.parent / 'cases'
CASE_PATH = CASES_DIR / 'conversation_journey_v1.jsonl'
FIXTURE_PATH = CASES_DIR / 'conversation_journey_v1.fixture.json'
RESEARCH_PATH = CASES_DIR / 'research_v1.jsonl'

DETERMINISTIC = 'deterministic'
SEMANTIC = 'semantic'

# scorer: deterministic checks are decided by code from captured runs and DB state;
# semantic checks need human review or a calibrated judge and never pass by default.
CHECKS: dict[str, dict[str, str]] = {
    'same_session': {'scorer': DETERMINISTIC, 'meaning': '每案例新 Session，所有輪次使用同一 Session。'},
    'owner_isolation': {'scorer': DETERMINISTIC, 'meaning': '實際查詢與執行只使用 owner_a 的資源，未解析到 owner_b 的同名投組。'},
    'no_holdings_mutation': {'scorer': DETERMINISTIC, 'meaning': '案例前後持股、交易與現金沒有改變。'},
    'trace_complete': {'scorer': DETERMINISTIC, 'meaning': '每輪有可歸因的原始輸出與必要執行證據；缺失記為 unverifiable。'},
    'resource_binding': {'scorer': DETERMINISTIC, 'meaning': 'Run input、工具有效 scope 與資料快照對應 portfolio_ref 的實際 ID。'},
    'no_premature_execution': {'scorer': DETERMINISTIC, 'meaning': '必要參數未明時提出澄清，不先建立猜測的 Run。'},
    'owned_portfolio_list': {'scorer': DETERMINISTIC, 'meaning': '列出或澄清的選項只包含目前使用者的兩個投組。'},
    'selection_retained': {'scorer': DETERMINISTIC, 'meaning': '之後有需要的輪次解析到已選定的投組。'},
    'reset_boundary': {'scorer': DETERMINISTIC, 'meaning': '重置前的指代、任務與證據不再作為新輪次的依據。'},
    'computed_comparison': {'scorer': DETERMINISTIC, 'meaning': '基準與方案共用快照與期間；獨立重算權重、NAV 與風險差異。'},
    'research_grounded': {'scorer': DETERMINISTIC, 'meaning': '依 research_case_refs 的已核實事實與來源檢查研究答案。'},
    'no_unsupported_claims': {'scorer': SEMANTIC, 'meaning': '不把財報當成未來報酬或波動的保證，不捏造數字與未取得的資料。'},
    'research_to_exposure': {'scorer': SEMANTIC, 'meaning': '前輪有來源的財報事實與本輪讀取的持倉形成可追蹤的影響說明。'},
    'impact_uncertainty': {'scorer': SEMANTIC, 'meaning': '區分已知事實、直接與未知間接曝險、條件性推論與歷史風險。'},
    'objective_retained': {'scorer': SEMANTIC, 'meaning': '降低波動的目標在補參數或切換後沒有遺失。'},
    'tradeoffs': {'scorer': SEMANTIC, 'meaning': '說明現金、集中度、曝險變動與改善限制。'},
    'limitations_disclosed': {'scorer': SEMANTIC, 'meaning': '說明資料不足與影響，保留可支持的資訊，不偽造完整計算。'},
}

# Checks whose failure reflects a feature that does not exist yet rather than a regression.
CAPABILITY_GAPS: dict[str, str] = {}  # Research evidence transfer is implemented; semantic verification remains pending.


FAILURE_CLASSES = ('bug', 'capability_gap', 'dependency_failed')

INTENTS: dict[str, dict[str, Any]] = {
    'list_portfolios': {'workflow': None, 'creates_run': False},
    'select_portfolio': {'workflow': None, 'creates_run': False, 'needs': ('portfolio_ref',)},
    'clarify': {'workflow': None, 'creates_run': False, 'needs': ('missing',)},
    'reset': {'workflow': None, 'creates_run': False},
    'research_answer': {'workflow': 'Research', 'creates_run': True, 'needs': ('ticker', 'research_case_refs')},
    'portfolio_diagnosis': {'workflow': 'Portfolio', 'creates_run': True, 'needs': ('portfolio_ref',)},
    'portfolio_impact': {'workflow': 'Portfolio', 'creates_run': True, 'needs': ('portfolio_ref', 'ticker', 'evidence_from_turns')},
    'risk_reduction': {'workflow': 'Portfolio', 'creates_run': True, 'needs': ('portfolio_ref',)},
    'insufficient_data': {'workflow': 'Portfolio', 'creates_run': True, 'needs': ('portfolio_ref',)},
}

EXPECTED_KEYS = {'intent', 'portfolio_ref', 'ticker', 'research_case_refs', 'evidence_from_turns',
                 'constraints', 'missing', 'checks'}
CASE_KEYS = {'id', 'title', 'fixture', 'review_status', 'regression_for', 'max_user_turns',
             'research_case_refs', 'case_checks', 'turns', 'conversation_success'}
CONSTRAINT_MODES = {'cash', 'holding'}


def read_jsonl(path: str | Path) -> list[dict[str, Any]]:
    return [json.loads(line) for line in Path(path).read_text(encoding='utf-8').splitlines() if line.strip()]


def load_fixture(path: str | Path = FIXTURE_PATH) -> dict[str, Any]:
    return json.loads(Path(path).read_text(encoding='utf-8'))


def validate_fixture(fixture: dict[str, Any]) -> None:
    if fixture.get('as_of_policy') != 'seed_date_frozen':
        raise ValueError('fixture as_of_policy must be seed_date_frozen')
    if fixture.get('numeric_golden') is not None:
        raise ValueError('numeric goldens must not be prefilled before independent verification')
    owned = set(fixture['owners']['owner_a']['portfolios'])
    owned_names = set(fixture['owners']['owner_a']['portfolios'].values())
    decoy_names = set(fixture['owners']['owner_b']['portfolios'].values())
    if owned_names != decoy_names:
        raise ValueError('owner_b decoys must reuse owner_a portfolio names to test isolation')
    securities = fixture['securities']
    for name, profile in fixture['profiles'].items():
        if type(profile.get('price_points')) is not int or profile['price_points'] < 2:
            raise ValueError('price_points must be an integer >= 2')
        if set(profile['weights']) != owned:
            raise ValueError(f'profile {name} must define weights for every owner_a portfolio')
        for alias, weights in profile['weights'].items():
            if not weights or any(type(v) not in (int, float) or not math.isfinite(v) for v in weights.values()):
                raise ValueError('weights must be finite numbers')
            if abs(sum(weights.values()) - 1) > 1e-9:
                raise ValueError(f'profile {name}/{alias} weights must sum to 1')
            if any(ticker not in securities for ticker in weights):
                raise ValueError(f'profile {name}/{alias} uses a ticker without a security definition')
            if any(not 0 < weight <= 1 for weight in weights.values()):
                raise ValueError(f'profile {name}/{alias} has an invalid weight')


def _research_index(path: str | Path) -> dict[str, dict[str, Any]]:
    return {row['id']: row for row in read_jsonl(path)}


def _validate_constraints(case_id: str, turn: str, constraints: dict[str, Any], weights: dict[str, float]) -> None:
    if constraints.get('preserve_other_holdings') is not True:
        raise ValueError(f'{case_id}/{turn}: preserve_other_holdings must be true')
    mode = constraints.get('mode')
    if mode not in CONSTRAINT_MODES:
        raise ValueError(f'{case_id}/{turn}: unsupported constraint mode {mode!r}')
    source = constraints.get('source_ticker')
    target = constraints.get('target_weight')
    if source not in weights:
        raise ValueError(f'{case_id}/{turn}: reduction source {source} is not held')
    if type(target) not in (int, float) or not math.isfinite(target) or not 0 <= target < weights[source]:
        raise ValueError(f'{case_id}/{turn}: target weight must be below the current weight of {source}')
    transfer = constraints.get('transfer_to')
    if mode == 'holding':
        if transfer not in weights or transfer == source:
            raise ValueError(f'{case_id}/{turn}: holding mode must transfer into another held security')
    elif transfer is not None:
        raise ValueError(f'{case_id}/{turn}: cash mode must not name a transfer target')


def validate_cases(cases: list[dict[str, Any]], fixture: dict[str, Any],
                   research: dict[str, dict[str, Any]]) -> None:
    validate_fixture(fixture)
    if not cases:
        raise ValueError('cases must not be empty')
    ids = [case.get('id') for case in cases]
    if len(ids) != len(set(ids)):
        raise ValueError('duplicate conversation case IDs')
    owned = fixture['owners']['owner_a']['portfolios']
    for case in cases:
        case_id = case.get('id', '<missing>')
        if not re.fullmatch(r'journey-\d{2}', case_id):
            raise ValueError(f'{case_id}: id must look like journey-NN')
        unknown = set(case) - CASE_KEYS
        if unknown:
            raise ValueError(f'{case_id}: unknown case keys {sorted(unknown)}')
        if case.get('review_status') not in {'draft', 'verified'}:
            raise ValueError(f'{case_id}: review_status must be draft or verified')
        # review_status is human contract review; numeric verification lives in the seed manifest.
        profile = fixture['profiles'].get(case.get('fixture'))
        if profile is None:
            raise ValueError(f'{case_id}: unknown fixture profile {case.get("fixture")!r}')
        if not case.get('conversation_success'):
            raise ValueError(f'{case_id}: conversation_success must describe the goal')
        for check in case.get('case_checks', []):
            if check not in CHECKS:
                raise ValueError(f'{case_id}: unknown case check {check}')
        turns = case.get('turns') or []
        if not turns:
            raise ValueError(f'{case_id}: turns must not be empty')
        if not {'same_session', 'owner_isolation', 'no_holdings_mutation', 'trace_complete'} <= set(case.get('case_checks', [])):
            raise ValueError(f'{case_id}: missing required case checks')
        if type(case.get('max_user_turns')) is not int or case.get('max_user_turns') != len(turns):
            raise ValueError(f'{case_id}: max_user_turns must equal the number of user turns')
        seen: list[str] = []
        reset_at: int | None = None
        refs_used: set[str] = set()
        for index, turn in enumerate(turns, 1):
            name = turn.get('turn')
            if name != f't{index}':
                raise ValueError(f'{case_id}: turns must be numbered t1..tN in order')
            if not turn.get('user', '').strip():
                raise ValueError(f'{case_id}/{name}: user message is required')
            expected = turn.get('expected') or {}
            unknown = set(expected) - EXPECTED_KEYS
            if unknown:
                raise ValueError(f'{case_id}/{name}: unknown expected keys {sorted(unknown)}')
            intent = INTENTS.get(expected.get('intent'))
            if intent is None:
                raise ValueError(f'{case_id}/{name}: unknown intent {expected.get("intent")!r}')
            for field in intent.get('needs', ()):
                if not expected.get(field):
                    raise ValueError(f'{case_id}/{name}: intent {expected["intent"]} requires {field}')
            ref = expected.get('portfolio_ref')
            if ref is not None and ref not in owned:
                raise ValueError(f'{case_id}/{name}: portfolio_ref {ref} is not an owner_a portfolio')
            if intent['workflow'] is None and any(k in expected for k in ('ticker', 'research_case_refs', 'constraints')):
                raise ValueError(f'{case_id}/{name}: turns that create no run cannot carry run parameters')
            if not expected.get('checks'):
                raise ValueError(f'{case_id}/{name}: checks must not be empty')
            for check in expected.get('checks', []):
                if check not in CHECKS:
                    raise ValueError(f'{case_id}/{name}: unknown check {check}')
            for evidence in expected.get('evidence_from_turns', []):
                if evidence not in seen:
                    raise ValueError(f'{case_id}/{name}: evidence_from_turns must reference an earlier turn')
                if reset_at is not None and int(evidence[1:]) <= reset_at:
                    raise ValueError(f'{case_id}/{name}: evidence cannot cross a reset boundary')
            for research_ref in expected.get('research_case_refs', []):
                source = research.get(research_ref)
                if source is None:
                    raise ValueError(f'{case_id}/{name}: unknown research case {research_ref}')
                if not source.get('verified') or source.get('excluded_reason'):
                    raise ValueError(f'{case_id}/{name}: research case {research_ref} must be verified and not excluded')
                if source.get('ticker') != expected.get('ticker'):
                    raise ValueError(f'{case_id}/{name}: research case {research_ref} is for a different ticker')
                refs_used.add(research_ref)
            if 'constraints' in expected:
                _validate_constraints(case_id, name, expected['constraints'], profile['weights'][ref])
            if expected['intent'] == 'reset':
                reset_at = index
            seen.append(name)
        if sorted(refs_used) != sorted(case.get('research_case_refs', [])):
            raise ValueError(f'{case_id}: research_case_refs must list exactly the refs used by its turns')


def summarize(cases: list[dict[str, Any]]) -> dict[str, Any]:
    deterministic = semantic = 0
    for case in cases:
        for check in case.get('case_checks', []):
            deterministic += CHECKS[check]['scorer'] == DETERMINISTIC
            semantic += CHECKS[check]['scorer'] == SEMANTIC
        for turn in case['turns']:
            for check in turn['expected'].get('checks', []):
                deterministic += CHECKS[check]['scorer'] == DETERMINISTIC
                semantic += CHECKS[check]['scorer'] == SEMANTIC
    return {'cases': len(cases), 'turns': sum(len(c['turns']) for c in cases),
            'deterministic_checks': deterministic, 'semantic_checks': semantic,
            'draft_cases': sum(c['review_status'] == 'draft' for c in cases)}


def main(argv: list[str] | None = None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    if argv and argv[0] in ('seed', 'run', 'rescore'):
        from .conversation_cli import main as runtime_main
        return runtime_main(argv)
    parser = argparse.ArgumentParser(prog='equitylens_eval.conversation')
    sub = parser.add_subparsers(dest='command', required=True)
    validate = sub.add_parser('validate', help='validate the conversation journey cases and fixture spec')
    validate.add_argument('--cases', default=str(CASE_PATH))
    validate.add_argument('--fixture', default=str(FIXTURE_PATH))
    validate.add_argument('--research', default=str(RESEARCH_PATH))
    for command in ('seed', 'run', 'rescore'):
        sub.add_parser(command, help=f'{command} conversation fixtures/evidence (use {command} --help)')
    args = parser.parse_args(argv)
    cases = read_jsonl(args.cases)
    validate_cases(cases, load_fixture(args.fixture), _research_index(args.research))
    print(json.dumps(summarize(cases), ensure_ascii=False))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
