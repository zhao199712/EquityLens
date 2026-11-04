"""Synthetic, owner-isolated journey fixtures. No production data is changed.

One profile per database/API. Research corpus is supplied separately, never
fabricated. A private manifest is reserved before the first account is created.
"""
from __future__ import annotations

import datetime as dt
from decimal import Decimal
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
import uuid

import httpx

from .portfolio import fixture, independent_metrics, require_local
from .portfolio_seed import sql_quote as q

VERSION = 1


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False,
                                     allow_nan=False, separators=(',', ':')).encode()).hexdigest()


def utc_day():
    return dt.datetime.now(dt.timezone.utc).date().isoformat()


def private_json(path, value):
    with open(path, 'x', encoding='utf-8', opener=lambda p, flags: os.open(p, flags, 0o600)) as f:
        json.dump(value, f, ensure_ascii=False, allow_nan=False, indent=2)


class Database:
    """Explicit Docker/DB binding; no connection strings or credentials in output."""
    def __init__(self, container, database, username):
        if not database.endswith('_eval'):
            raise ValueError('Use a dedicated database ending in _eval')
        info = json.loads(subprocess.check_output(['docker', 'inspect', container], text=True))[0]
        project = (info['Config'].get('Labels') or {}).get('com.docker.compose.project')
        if project not in ('equitylens-runtime', 'equitylens-portfolio-eval', 'equitylens-conversation-eval'):
            raise ValueError('Refusing non-isolated database container')
        self.config = dict(container=container, database=database, username=username)

    def sql(self, statement, *, write=False):
        if not write:
            statement = 'BEGIN READ ONLY;\n' + statement + '\nCOMMIT;'
        r = subprocess.run(['docker', 'exec', '-i', self.config['container'], 'psql',
                            '-X', '-q', '-v', 'ON_ERROR_STOP=1', '-tA',
                            '-U', self.config['username'], '-d', self.config['database']],
                           input=statement, text=True, capture_output=True)
        if r.returncode:
            raise RuntimeError('Isolated database operation failed (raw diagnostics withheld)')
        return r.stdout.strip()

    def json(self, query):
        return json.loads(self.sql(query))

    def bind(self, user):
        uid = str(uuid.UUID(user['id']))
        if self.sql(f"SELECT count(*) FROM app_user WHERE id={q(uid)} AND email={q(user['email'])};") != '1':
            raise ValueError('API identity does not match the isolated database')


def build_fixture(spec, profile, as_of):
    """Shared prices, actual rounded quantities, and independently calculated baseline."""
    source = fixture('short' if profile == 'short_history' else 'concentrated', as_of)
    count = spec['profiles'][profile]['price_points']
    if len(source['dates']) != count:
        raise ValueError('Unsupported price_points for this generator version')
    prices = {ticker: a['prices'] for ticker, a in zip(spec['securities'], source['assets'])}
    portfolios = {}
    for alias, weights in spec['profiles'][profile]['weights'].items():
        assets = []
        for ticker, weight in weights.items():
            price = prices[ticker][-1]
            qty = float((Decimal(str(weight)) * Decimal(100000) / Decimal(str(price))).quantize(Decimal('.000001')))
            assets.append(dict(ticker=ticker, quantity=qty, prices=prices[ticker], valuationPrice=price))
        snap = dict(dates=source['dates'], assets=assets, cash=0, as_of=as_of, priceBasis='AdjustedClose')
        nav = sum(a['quantity'] * a['valuationPrice'] for a in assets)
        actual = {a['ticker']: a['quantity'] * a['valuationPrice'] / nav for a in assets}
        portfolios[alias] = dict(snapshot=snap, nav=nav, weights=actual,
                                 metrics=independent_metrics(snap, actual))
    return dict(version=VERSION, profile=profile, as_of=as_of, dates=source['dates'],
                prices=prices, portfolios=portfolios)


def scenario_golden(portfolio, constraints):
    weights = dict(portfolio['weights'])
    source = constraints['source_ticker']
    released = weights[source] - constraints['target_weight']
    weights[source] = constraints['target_weight']
    if constraints['mode'] == 'holding':
        weights[constraints['transfer_to']] += released
    return dict(weights=weights, cashWeight=1-sum(weights.values()),
                metrics=independent_metrics(portfolio['snapshot'], weights))


def corpus_state(db, proof):
    """Proof attests human review; fingerprint includes text AND embeddings, not IDs alone."""
    if not proof:
        return None
    if not proof.get('version') or not proof.get('case_refs') or not proof.get('documents'):
        raise ValueError('Corpus proof requires version, case_refs and documents {document UUID: file SHA256}')
    ids = ','.join(q(str(uuid.UUID(x))) for x in proof['documents'])
    rows = db.json(f"""
SELECT coalesce(jsonb_agg(x ORDER BY x.id),'[]'::jsonb) FROM (
 SELECT d.id, d.document_type, d.title, d.source_url, u.sha256_hash,
   (SELECT count(*) FROM document_chunk c WHERE c.document_id=d.id) AS chunks,
   (SELECT count(*) FROM document_chunk c JOIN document_embedding e ON e.document_chunk_id=c.id
      WHERE c.document_id=d.id AND e.embedding IS NOT NULL) AS embeddings,
   (SELECT md5(coalesce(string_agg(c.page_number::text || ':' || c.content || ':' ||
      coalesce(e.embedding_model,'') || ':' || coalesce(e.embedding::text,''), '|' ORDER BY c.chunk_index),''))
    FROM document_chunk c LEFT JOIN document_embedding e ON e.document_chunk_id=c.id
    WHERE c.document_id=d.id) AS content_fingerprint
 FROM document d JOIN uploaded_file u ON u.id=d.uploaded_file_id WHERE d.id IN ({ids})
) x;""")
    if len(rows) != len(proof['documents']) or any(
        r['sha256_hash'] != proof['documents'][r['id']] or
        not r['chunks'] or r['chunks'] != r['embeddings'] for r in rows
    ):
        raise ValueError('Reviewed corpus is missing, changed, or incompletely embedded')
    return dict(proof=proof, fingerprint=digest(rows), documents=rows)


def seed_sql(spec, frozen, entries, securities):
    commands = ['BEGIN;']
    for ticker, sid in securities.items():
        meta = spec['securities'][ticker]
        # Caller has verified identity/metadata; an existing corpus security is reused.
        commands.append(f"""INSERT INTO security(id,ticker,exchange,name,currency,asset_type)
VALUES({q(sid)},{q(ticker)},{q(meta['exchange'])},{q(ticker)},{q(meta['currency'])},{q(meta['asset_type'])})
ON CONFLICT(id) DO NOTHING;""")
        rows = [f"({q(sid)},{q(date+'T00:00:00Z')},'1d',{p},{p},{p},{p},{p},'conversation-eval-v1')"
                for date, p in zip(frozen['dates'], frozen['prices'][ticker])]
        commands.append('INSERT INTO market_price(security_id,price_time,interval,open,high,low,close,adjusted_close,data_source) VALUES ' + ','.join(rows) + ';')
    for entry in entries.values():
        for owner in ('owner_a', 'owner_b'):
            user = entry[owner]
            for ref, pid in user['portfolios'].items():
                alias = ref.removeprefix('decoy_')
                commands.append(f"INSERT INTO portfolio(id,owner_user_id,name,base_currency) VALUES({q(pid)},{q(user['id'])},{q(spec['owners'][owner]['portfolios'][ref])},'TWD');")
                cost = Decimal(0)
                for a in frozen['portfolios'][alias]['snapshot']['assets']:
                    sid, qty = securities[a['ticker']], Decimal(str(a['quantity']))
                    cost += qty * Decimal(100)
                    commands.append(f"INSERT INTO portfolio_holding(portfolio_id,security_id,quantity,average_cost,cost_currency) VALUES({q(pid)},{q(sid)},{qty},100,'TWD');")
                    commands.append(f"INSERT INTO portfolio_transaction(portfolio_id,security_id,transaction_type,quantity,price,transaction_date) VALUES({q(pid)},{q(sid)},'BUY',{qty},100,{q(frozen['dates'][0])});")
                commands.append(f"INSERT INTO portfolio_cash_flow(portfolio_id,flow_type,amount,currency,effective_date,status) VALUES({q(pid)},'Deposit',{cost},'TWD',{q(frozen['dates'][0])},'Applied');")
    commands.append('COMMIT;')
    return '\n'.join(commands)


def seed(args, cases, spec, *, db=None, client=None):
    require_local(args.base_url)
    if args.as_of != utc_day():
        raise ValueError('Live seed must freeze the current UTC date')
    db = db or Database(args.container, args.database, args.username)
    selected = [c for c in cases if c['fixture'] == args.profile]
    frozen = build_fixture(spec, args.profile, args.as_of)
    proof = json.loads(Path(args.corpus).read_text()) if args.corpus else None
    corpus = corpus_state(db, proof)
    securities = {}
    for ticker, meta in spec['securities'].items():
        rows = db.json(f"SELECT coalesce(jsonb_agg(x),'[]'::jsonb) FROM (SELECT id,exchange,currency,asset_type FROM security WHERE ticker={q(ticker)}) x;")
        if len(rows) > 1 or (rows and any(rows[0][key] != meta[key] for key in ('exchange', 'currency', 'asset_type'))):
            raise ValueError(f'Ambiguous or incompatible security identity: {ticker}')
        sid = rows[0]['id'] if rows else str(uuid.uuid4())
        if db.sql(f"SELECT count(*) FROM market_price WHERE security_id={q(sid)};") != '0':
            raise ValueError('Fixture tickers already have prices; use a fresh profile database')
        securities[ticker] = sid
    manifest = dict(version=VERSION, suite=spec['suite'], state='seeding', profile=args.profile,
                    api_url=args.base_url, db=db.config, as_of=args.as_of, cases={},
                    fixture=frozen, fixture_hash=digest(frozen), spec_hash=digest(spec),
                    corpus=corpus, securities=securities)
    private_json(args.manifest, manifest)  # Reserve before any mutation.
    own_client = client is None
    client = client or httpx.Client(base_url=args.base_url, timeout=60)
    try:
        for case in selected:
            entry = {}
            manifest['cases'][case['id']] = entry
            for owner in ('owner_a', 'owner_b'):
                email = f"journey-eval-{uuid.uuid4().hex}@example.invalid"
                password = secrets.token_urlsafe(24) + 'aA1!'
                r = client.post('/api/auth/register', json=dict(email=email, password=password, displayName='Eval'))
                r.raise_for_status()
                auth = r.json()
                user = dict(id=auth['user']['id'], email=email, password=password,
                            portfolios={ref: str(uuid.uuid4()) for ref in spec['owners'][owner]['portfolios']})
                entry[owner] = user
                db.bind(user)
            entry['goldens'] = {
                t['turn']: scenario_golden(frozen['portfolios'][t['expected']['portfolio_ref']], t['expected']['constraints'])
                for t in case['turns'] if t['expected'].get('constraints')
            }
        db.sql(seed_sql(spec, frozen, manifest['cases'], securities), write=True)
        manifest['state'] = 'ready'
        manifest['numeric_verification'] = 'independent-python-v1'
    finally:
        # Retain partial identities for diagnosis if registration or the transaction failed.
        with open(args.manifest, 'w', encoding='utf-8') as f:
            json.dump(manifest, f, ensure_ascii=False, allow_nan=False, indent=2)
        if own_client:
            client.close()
    return manifest
