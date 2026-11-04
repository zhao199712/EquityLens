"""CLI composition; run artifacts contain no private fixture credentials."""
from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
import subprocess

from .conversation import CASE_PATH, FIXTURE_PATH, RESEARCH_PATH, read_jsonl, load_fixture, _research_index, validate_cases
from .conversation_seed import Database, corpus_state, digest, private_json, q, seed, utc_day
from .conversation_scoring import VERSION, result, score_case, summarize


def parser():
    p = argparse.ArgumentParser(prog='python -m equitylens_eval.conversation')
    sub = p.add_subparsers(dest='command', required=True)
    for command in ('seed', 'run', 'rescore'):
        c = sub.add_parser(command)
        c.add_argument('--cases', type=Path, default=CASE_PATH)
        c.add_argument('--fixture', type=Path, default=FIXTURE_PATH)
        c.add_argument('--research', type=Path, default=RESEARCH_PATH)
        if command == 'seed':
            c.add_argument('--profile', choices=('standard', 'no_direct', 'short_history'), required=True)
            for flag in ('base-url', 'container', 'database', 'username', 'manifest'):
                c.add_argument('--' + flag, required=True)
            c.add_argument('--corpus', type=Path, help='Reviewed corpus proof; optional for journey-00')
            c.add_argument('--as-of', default=utc_day())
        else:
            c.add_argument('--out', type=Path, required=True)
            if command == 'run':
                c.add_argument('--manifest', type=Path, action='append', required=True)
                c.add_argument('--case', action='append')
                c.add_argument('--include-unverified', action='store_true')
                c.add_argument('--timeout', type=float, default=360)
                c.add_argument('--system-commit', required=True, help='Actual API build SHA, not the harness SHA')
                c.add_argument('--usage-ledger', type=Path, required=True, help='Externally enforced provider budget ledger')
            else:
                c.add_argument('--responses', type=Path, required=True)
    return p


def public_manifest(manifest):
    return {k: v for k, v in manifest.items() if k not in ('db', 'cases', 'api_url', 'securities')} | {
        'cases': {cid: {owner: {k: v for k, v in entry[owner].items() if k in ('id', 'portfolios')}
                       for owner in ('owner_a', 'owner_b')} for cid, entry in manifest['cases'].items()}}


def git_sha():
    return subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()


def verify_prices(db, manifest):
    """Reject mixed profiles, external synchronization, extra dates and stale prices."""
    for ticker, sid in manifest['securities'].items():
        rows = db.json(f"""SELECT coalesce(jsonb_agg(x ORDER BY x.date),'[]'::jsonb) FROM (
SELECT (price_time AT TIME ZONE 'UTC')::date::text AS date,close,adjusted_close,interval,data_source
FROM market_price WHERE security_id={q(sid)}) x;""")
        expected = list(zip(manifest['fixture']['dates'], manifest['fixture']['prices'][ticker]))
        if len(rows) != len(expected) or any(
            r['date'] != day or r['interval'] != '1d' or r['data_source'] != 'conversation-eval-v1'
            or abs(float(r['close'])-price) > .000001 or r['adjusted_close'] is None
            or abs(float(r['adjusted_close'])-price) > .000001
            for r, (day, price) in zip(rows, expected)
        ):
            raise ValueError('Fixture prices changed; use a fresh isolated profile database')


def report(out, cases, responses, manifests, research):
    index = {cid: m for m in manifests for cid in m['cases']}
    by_id = {r['case_id']: r for r in responses}
    if len(by_id) != len(responses):
        raise ValueError('Duplicate case response')
    results = [score_case(c, by_id[c['id']], index[c['id']]['cases'][c['id']],
                          index[c['id']]['fixture'], research) for c in cases if c['id'] in by_id]
    # Session uniqueness is a suite-level invariant, not just a per-case check.
    sessions = [r.get('session_id') for r in responses if r.get('session_id')]
    reused = {s for s in sessions if sessions.count(s) > 1}
    for r in results:
        if by_id[r['case_id']].get('session_id') in reused:
            r['checks'].append(result('same_session', False, 'responses/*/session_id',
                                      'Session reused across cases', 'bug'))
            r.update(status='fail', conversation_success=False if r['review_status'] == 'verified' else None)
    summary = summarize(results)
    summary['not_run'] = [c['id'] for c in cases if c['id'] not in by_id]
    private_json(out/'summary.json', summary)
    lines = ['# Conversation eval', '',
             f"Cases: {len(results)}; failed: {summary['failed']}; not run: {len(summary['not_run'])}.",
             f"Conversation success: {summary['conversation_success_rate'] if summary['conversation_success_rate'] is not None else 'N/A'} "
             f"(eligible cases: {summary['conversation_denominator']}).", '',
             'Semantic checks require review. Draft cases never establish full conversation success.',
             'Total API cost: unknown; no judge was called.', '',
             '| Case | Status | Failed checks | Unverifiable |', '|---|---|---|---|']
    for r in results:
        lines.append(f"| {r['case_id']} | {r['status']} | " +
                     ', '.join(c['check'] for c in r['checks'] if c['status'] == 'fail') + ' | ' +
                     str(sum(c['status'] == 'unverifiable' for c in r['checks'])) + ' |')
    (out/'report.md').write_text('\n'.join(lines)+'\n', encoding='utf-8')
    return summary


def load_manifests(paths, spec):
    manifests = [json.loads(p.read_text()) for p in paths]
    seen, dbs, urls, profiles = set(), set(), set(), set()
    for m in manifests:
        if m.get('state') != 'ready' or m.get('suite') != spec['suite'] or m.get('version') != 1:
            raise ValueError('Manifest is not a ready journey fixture')
        if m['fixture_hash'] != digest(m['fixture']) or m['spec_hash'] != digest(spec):
            raise ValueError('Fixture/spec hash mismatch')
        if m['as_of'] != utc_day():
            raise ValueError('Live runs require today UTC seed; old evidence can be rescored')
        identity = (m['db']['container'], m['db']['database'])
        if (identity in dbs or m['api_url'] in urls or seen.intersection(m['cases']) or m['profile'] in profiles
                or m['fixture']['profile'] != m['profile'] or m['fixture']['as_of'] != m['as_of']):
            raise ValueError('Profiles must have distinct databases/APIs and unique cases')
        dbs.add(identity); urls.add(m['api_url']); seen.update(m['cases']); profiles.add(m['profile'])
    return manifests


def run(args, cases, spec, research):
    import httpx
    from .portfolio import provider_failures, require_local
    from .conversation_runner import AuthClient, Observer, run_case
    if args.timeout <= 0 or not math.isfinite(args.timeout):
        raise ValueError('timeout must be positive and finite')
    if os.environ.get('CONVERSATION_EVAL_BUDGET_ENFORCED') != '1' or not args.usage_ledger.is_file():
        raise ValueError('Live mode requires an externally budgeted API and its usage ledger')
    try:
        initial_ledger = [json.loads(line) for line in args.usage_ledger.read_text().splitlines() if line.strip()]
    except (OSError, ValueError):
        raise ValueError('Provider usage ledger is unreadable or incomplete') from None
    if provider_failures(initial_ledger):
        raise ValueError('Provider usage ledger contains failures or unsettled requests; resolve them before running')
    manifests = load_manifests(args.manifest, spec)
    index = {cid: m for m in manifests for cid in m['cases']}
    if args.case and set(args.case) - {c['id'] for c in cases}:
        raise ValueError('Unknown case selection')
    selected = [c for c in cases if (not args.case or c['id'] in args.case)]
    unavailable = [c['id'] for c in selected if c['id'] not in index]
    if unavailable:
        raise ValueError('Missing profile manifest for selected cases: ' + ', '.join(unavailable))
    if any(c['fixture'] != index[c['id']]['profile'] for c in selected):
        raise ValueError('Case bound to a different fixture profile')
    selected = [c for c in selected if args.include_unverified or c['review_status'] == 'verified']
    if not selected:
        raise ValueError('No eligible cases; draft diagnostics require --include-unverified')
    connections, corpus_ok = {}, {}
    for m in manifests:
        require_local(m['api_url'])
        db = Database(**m['db'])
        verify_prices(db, m)
        connections[m['profile']] = db
        try:
            current = corpus_state(db, (m.get('corpus') or {}).get('proof'))
            corpus_ok[m['profile']] = current is not None and current['fingerprint'] == m['corpus']['fingerprint']
        except (ValueError, RuntimeError):
            corpus_ok[m['profile']] = False
    args.out.mkdir(parents=True, exist_ok=False, mode=0o700)
    saved_manifests = [public_manifest(m) for m in manifests]
    meta = dict(suite=spec['suite'], system_commit=args.system_commit, harness_commit=git_sha(),
                cases_hash=digest(selected), research_hash=digest(research),
                fixtures_hash=digest(saved_manifests), fixture_hashes=[m['fixture_hash'] for m in manifests],
                harness_source_hash=digest({p.name: p.read_text() for p in Path(__file__).parent.glob('*.py')}),
                harness_dirty=bool(subprocess.check_output(['git', 'status', '--porcelain'], text=True)),
                model_cost='unknown; external ledger', selected=[c['id'] for c in selected])
    private_json(args.out/'run_meta.json', meta)
    private_json(args.out/'score_meta.json', dict(scorer_version=VERSION, scorer_commit=git_sha(),
                                                scorer_source_hash=meta['harness_source_hash']))
    private_json(args.out/'cases.json', selected)
    private_json(args.out/'research.json', research)
    private_json(args.out/'fixtures.json', saved_manifests)
    responses = []
    with open(args.out/'responses.jsonl', 'x', encoding='utf-8', opener=lambda p, flags: os.open(p, flags, 0o600)) as output, \
         open(args.out/'checkpoints.jsonl', 'x', encoding='utf-8', opener=lambda p, flags: os.open(p, flags, 0o600)) as journal:
        def checkpoint(raw):
            journal.write(json.dumps(raw, ensure_ascii=False, allow_nan=False)+'\n')
            journal.flush()
        for case in selected:
            m, cid = index[case['id']], case['id']
            refs = set((m.get('corpus') or {}).get('proof', {}).get('case_refs', []))
            offset = len(args.usage_ledger.read_text().splitlines())
            if case['research_case_refs'] and (
                not corpus_ok[m['profile']] or not set(case['research_case_refs']).issubset(refs)
            ):
                raw = dict(case_id=cid, error='research_corpus_unavailable', turns=[
                    dict(turn=t['turn'], status='not_run', error='dependency_failed') for t in case['turns']])
            else:
                entry = m['cases'][cid]
                try:
                    with httpx.Client(base_url=m['api_url'], timeout=args.timeout) as client:
                        auth = AuthClient(client, entry['owner_a'])
                        raw = run_case(auth, Observer(connections[m['profile']], entry, m['fixture']), case,
                                       timeout=args.timeout, checkpoint=checkpoint)
                except (httpx.HTTPError, ValueError, RuntimeError) as exc:
                    raw = dict(case_id=cid, error=type(exc).__name__, turns=[])
            try:
                verify_prices(connections[m['profile']], m)
            except (ValueError, RuntimeError):
                raw['fixture_error'] = 'price_fixture_changed_or_unavailable'
            try:
                lines = args.usage_ledger.read_text().splitlines()
                ledger = [json.loads(s) for s in lines[offset:] if s.strip()]
                errors = provider_failures(ledger)
                if len(lines) < offset:
                    errors.append('usage_ledger_truncated')
            except (OSError, ValueError):
                errors = ['usage_ledger_unreadable_or_incomplete']
            if errors:
                raw['provider_errors'] = errors
            output.write(json.dumps(raw, ensure_ascii=False, allow_nan=False)+'\n'); output.flush()
            responses.append(raw)
            print(cid + ': evidence saved', flush=True)
            if errors or raw.get('fixture_error') or any(r.get('cancel_error') for r in raw.get('turns', [])):
                break
    return report(args.out, selected, responses, saved_manifests, research)


def rescore(args):
    source = args.responses.parent
    cases = json.loads((source/'cases.json').read_text())
    research = json.loads((source/'research.json').read_text())
    manifests = json.loads((source/'fixtures.json').read_text())
    meta = json.loads((source/'run_meta.json').read_text())
    if (digest(research) != meta['research_hash'] or digest(cases) != meta['cases_hash']
            or digest(manifests) != meta['fixtures_hash']):
        raise ValueError('Saved cases, research or fixture contract changed')
    responses = read_jsonl(args.responses)
    args.out.mkdir(parents=True, exist_ok=False, mode=0o700)
    # Preserve production provenance; scoring provenance is a separate artifact.
    for name in ('cases.json', 'research.json', 'fixtures.json', 'run_meta.json'):
        private_json(args.out/name, json.loads((source/name).read_text()))
    with open(args.out/'responses.jsonl', 'xb', opener=lambda p, flags: os.open(p, flags, 0o600)) as f:
        f.write(args.responses.read_bytes())
    private_json(args.out/'score_meta.json', dict(scorer_version=VERSION, scorer_commit=git_sha(),
                                                scorer_source_hash=digest({p.name: p.read_text() for p in Path(__file__).parent.glob('*.py')}),
                                                rescored_from=str(source.resolve())))
    return report(args.out, cases, responses, manifests, research)


def main(argv):
    args = parser().parse_args(argv)
    if args.command == 'rescore':
        summary = rescore(args)
    else:
        cases, spec, research = read_jsonl(args.cases), load_fixture(args.fixture), _research_index(args.research)
        validate_cases(cases, spec, research)
        if args.command == 'seed':
            seeded = seed(args, cases, spec)
            print(f"Seeded {len(seeded['cases'])} cases in profile {seeded['profile']}; private manifest saved")
            return 0
        summary = run(args, cases, spec, research)
    print(f"{summary['attempted']} cases; {summary['failed']} failed; full conversation success: "
          f"{summary['conversation_success_rate'] if summary['conversation_success_rate'] is not None else 'N/A'}")
    return 1 if summary['failed'] or summary['not_run'] else 0
