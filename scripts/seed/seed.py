#!/usr/bin/env python3
"""Versioned, data-only EquityLens development seed. Python standard library only.

Production is used exclusively by export's repeatable-read/read-only transaction.
Import accepts only a dedicated, labelled Docker container and a *_seed database.
No SQL, executable files, users or credentials are accepted from the data bundle.
"""
from __future__ import annotations

import argparse
import base64
import datetime as dt
import gzip
import hashlib
import json
import math
import os
from pathlib import Path
import re
import secrets
import struct
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
TABLES = ('security', 'market_price', 'taiwan_total_return_index',
          'financial_statement', 'financial_line_item', 'uploaded_file', 'document',
          'financial_filing', 'investor_conference', 'document_chunk', 'document_embedding')
PERSONAL = ('app_user', 'portfolio', 'portfolio_transaction', 'portfolio_cash_flow', 'portfolio_holding')
NAMESPACE = uuid.UUID('03ebc33d-1c63-4d45-adbf-70ff10826937')


def uid(name):
    return str(uuid.uuid5(NAMESPACE, name))


def run(command, data=None):
    p = subprocess.run(command, input=data, text=True, capture_output=True)
    if p.returncode:
        # SQL errors can echo financial text or password hashes. Do not print raw errors.
        raise RuntimeError(f'{command[0]} failed (exit {p.returncode}); raw output withheld')
    return p.stdout


def psql(container, database, sql, readonly=True):
    if not re.fullmatch(r'[A-Za-z0-9_-]+', container):
        raise ValueError('Invalid container name')
    if database and not re.fullmatch(r'[a-z][a-z0-9_]+', database):
        raise ValueError('Invalid database name')
    script = ('export PGOPTIONS="-c statement_timeout=120000 -c default_transaction_read_only=' +
              ('on' if readonly else 'off') + '"; '
              'exec psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d ' +
              (database if database else '"$POSTGRES_DB"'))
    return run(['docker', 'exec', '-i', container, 'sh', '-c', script], sql)


def literal(value):
    return "'" + str(value).replace("'", "''") + "'"


def json_sql(value):
    # Hex transport avoids COPY escaping, dollar quoting and shell interpolation.
    return "convert_from(decode('" + json.dumps(value, ensure_ascii=False).encode().hex() + "','hex'),'UTF8')::jsonb"


def read_selection(path):
    selection = json.loads(Path(path).read_text())
    if selection.get('policy') == 'top10-history-v2':
        expected = {'2330', '2454', '2317', '2308', '3711', '1303', '2383', '2881', '2891', '2303'}
        codes = [r['code'] for r in selection['constituents']]
        if len(codes) != 10 or set(codes) != expected or set(selection['selected']) != expected | {'0050', '^TWII'}:
            raise ValueError('Invalid top10 history selection')
        return selection
    codes = [r['code'] for r in selection['constituents']]
    if len(codes) != 50 or len(set(codes)) != 50 or any(not re.fullmatch(r'\d{4}', c) for c in codes):
        raise ValueError('Expected 50 distinct official constituent codes')
    if selection['excluded'] != ['6446'] or selection['additional'] != ['0050']:
        raise ValueError('This seed policy excludes 6446 and adds 0050')
    selection['selected'] = sorted((set(codes) - {'6446'}) | {'0050'})
    if len(selection['selected']) != 50:
        raise ValueError('Expected 50 selected instruments after exclusion/addition')
    return selection


def export(args):
    selection = read_selection(args.constituents)
    codes = ','.join(literal(c) for c in selection['selected'])
    sec = f"SELECT id FROM security WHERE exchange='TWSE' AND ticker IN ({codes})"
    research = "SELECT id FROM security WHERE exchange='TWSE' AND ticker IN ('2330','2317')"
    docs = f'SELECT document_id FROM financial_filing WHERE security_id IN ({research}) UNION SELECT document_id FROM investor_conference WHERE security_id IN ({research})'
    files = f'SELECT uploaded_file_id FROM document WHERE id IN ({docs}) UNION SELECT uploaded_file_id FROM financial_filing WHERE security_id IN ({research}) UNION SELECT uploaded_file_id FROM investor_conference WHERE security_id IN ({research})'
    filters = {
        'security': f'id IN ({sec})',
        'market_price': f"security_id IN ({sec}) AND interval='1d'",
        'taiwan_total_return_index': 'true',
        'financial_statement': f'security_id IN ({research})',
        'financial_line_item': f'financial_statement_id IN (SELECT id FROM financial_statement WHERE security_id IN ({research}))',
        'uploaded_file': f'id IN ({files})', 'document': f'id IN ({docs})',
        'financial_filing': f'security_id IN ({research})',
        'investor_conference': f'security_id IN ({research})',
        'document_chunk': f'document_id IN ({docs})',
        'document_embedding': f'document_chunk_id IN (SELECT id FROM document_chunk WHERE document_id IN ({docs}))',
    }
    sql = ['BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;']
    sql += [f"SELECT jsonb_build_object('table',{literal(t)},'rows',COALESCE(jsonb_agg(t),'[]'::jsonb)) FROM (SELECT * FROM {t} WHERE {filters[t]}) t;" for t in TABLES]
    sql += ['SELECT jsonb_build_object(\'migrations\',jsonb_agg("MigrationId" ORDER BY "MigrationId")) FROM "__EFMigrationsHistory";', 'COMMIT;']
    result = [json.loads(line) for line in psql(args.container, args.database, '\n'.join(sql)).splitlines() if line.startswith('{')]
    tables = {r['table']: r['rows'] for r in result if 'table' in r}
    # Strip source account ownership and object storage locations before writing any bytes.
    for row in tables['uploaded_file']:
        row.update(uploaded_by_user_id=None, bucket_name='seed-unavailable',
                   object_key=f"unavailable/{row['id']}.pdf", original_file_name=f"{row['id']}.pdf",
                   storage_provider='SeedMetadata', upload_status='Unavailable', sha256_hash=None)
    for row in tables['financial_filing']:
        row['uploaded_by_user_id'] = None
    for row in tables['investor_conference']:
        row['original_file_url'] = None
        row['original_file_name'] = None
    for table in ('document', 'financial_filing', 'investor_conference'):
        for row in tables[table]:
            url = row.get('source_url')
            if url and not url.startswith(('https://www.twse.com.tw/', 'https://mops.twse.com.tw/',
                                           'https://mopsov.twse.com.tw/', 'https://doc.twse.com.tw/',
                                           'https://www.tsmc.com/', 'https://investor.tsmc.com/',
                                           'https://www.honhai.com/')):
                row['source_url'] = None
    excluded_prices = []
    kept = []
    ticker_by_id = {r['id']: r['ticker'] for r in tables['security']}
    for r in tables['market_price']:
        if r['close'] <= 0 or (r['adjusted_close'] is not None and r['adjusted_close'] <= 0):
            excluded_prices.append({'ticker': ticker_by_id[r['security_id']],
                                    'date': r['price_time'][:10], 'reason': 'nonpositive_price'})
        else:
            kept.append(r)
    tables['market_price'] = kept
    dates = {}
    for row in tables['market_price']:
        dates.setdefault(row['security_id'], set()).add(row['price_time'][:10])
    common = set.intersection(*dates.values()) if dates else set()
    if not common:
        raise ValueError('No common price date')
    as_of = max(common)
    tables['market_price'] = [r for r in tables['market_price'] if r['price_time'][:10] <= as_of]
    tables['taiwan_total_return_index'] = [r for r in tables['taiwan_total_return_index'] if r['trading_date'] <= as_of]
    for t, rows in tables.items():
        rows.sort(key=lambda r: str(r.get('id', r.get('trading_date'))))
    bundle = {'formatVersion': 1, 'selection': selection, 'asOf': as_of,
              'migrations': result[-1]['migrations'], 'excludedPrices': excluded_prices, 'tables': tables}
    report = validate(bundle)
    out = Path(args.output)
    out.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(bundle, ensure_ascii=False, separators=(',', ':')).encode()
    compressed = gzip.compress(payload, mtime=0)
    name = 'equitylens-seed-v1.json.gz'
    (out / name).write_bytes(compressed)
    manifest = {'formatVersion': 1, 'asset': name, 'sha256': hashlib.sha256(compressed).hexdigest(),
                'bytes': len(compressed), 'asOf': as_of, 'selection': selection,
                'migrations': bundle['migrations'], 'validation': report,
                'pdfIncluded': False, 'exchangeRatesIncluded': False,
                'requiresPrivateDistribution': True}
    (out / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'asset': str(out / name), 'bytes': len(compressed), 'validation': report}, ensure_ascii=False, indent=2))


def validate(bundle):
    if bundle.get('formatVersion') != 1 or set(bundle['tables']) != set(TABLES):
        raise ValueError('Unsupported bundle format or table allowlist')
    t = bundle['tables']
    ids = {}
    for table, rows in t.items():
        keys = [r.get('id', r.get('trading_date')) for r in rows]
        if len(keys) != len(set(keys)):
            raise ValueError(f'Duplicate keys in {table}')
        ids[table] = set(keys)
    codes = {r['ticker'] for r in t['security']}
    if codes != set(bundle['selection']['selected']) or len(codes) != (12 if bundle['selection'].get('policy') == 'top10-history-v2' else 50) or '6446' in codes:
        raise ValueError('Security selection mismatch')
    if any(r['exchange'] != 'TWSE' or r['currency'] != 'TWD' for r in t['security']):
        raise ValueError('Only TWSE/TWD instruments are allowed')
    references = {'market_price': [('security_id', 'security')],
        'financial_statement': [('security_id', 'security')],
        'financial_line_item': [('financial_statement_id', 'financial_statement')],
        'document': [('uploaded_file_id', 'uploaded_file')],
        'financial_filing': [('security_id', 'security'), ('document_id', 'document'), ('uploaded_file_id', 'uploaded_file')],
        'investor_conference': [('security_id', 'security'), ('document_id', 'document'), ('uploaded_file_id', 'uploaded_file')],
        'document_chunk': [('document_id', 'document')],
        'document_embedding': [('document_chunk_id', 'document_chunk')]}
    for table, refs in references.items():
        for row in t[table]:
            for field, target in refs:
                if row.get(field) is not None and row[field] not in ids[target]:
                    raise ValueError(f'Orphan {table}.{field}')
    for table in ('financial_filing', 'uploaded_file'):
        if any(r.get('uploaded_by_user_id') is not None for r in t[table]):
            raise ValueError('Source user identity must be removed')
    if any(r['storage_provider'] != 'SeedMetadata' or r['upload_status'] != 'Unavailable' or
           r['object_key'] != f"unavailable/{r['id']}.pdf" or r['bucket_name'] != 'seed-unavailable'
           for r in t['uploaded_file']):
        raise ValueError('Source storage references must be removed')
    research_ids = {r['id'] for r in t['security'] if r['ticker'] in ('2330', '2317')}
    for table in ('financial_statement', 'financial_filing', 'investor_conference'):
        if any(r['security_id'] not in research_ids for r in t[table]):
            raise ValueError('Unexpected research issuer')
    dates = {r['id']: set() for r in t['security']}
    adjusted_missing = {}
    seen = set()
    for r in t['market_price']:
        key = (r['security_id'], r['price_time'][:10])
        if key in seen or r['interval'] != '1d' or key[1] > bundle['asOf']:
            raise ValueError('Duplicate, future or non-daily prices')
        seen.add(key)
        if not math.isfinite(float(r['close'])) or r['close'] <= 0:
            raise ValueError('Invalid closing price')
        if r['adjusted_close'] is None:
            adjusted_missing[r['security_id']] = adjusted_missing.get(r['security_id'], 0) + 1
        elif not math.isfinite(float(r['adjusted_close'])) or r['adjusted_close'] <= 0:
            raise ValueError('Invalid adjusted close')
        dates[r['security_id']].add(key[1])
    common = sorted(set.intersection(*dates.values()))
    if len(common) < 352:
        raise ValueError(f'Backtest needs 352 common dates, found {len(common)}')
    if common[-1] != bundle['asOf']:
        raise ValueError('asOf must be a common trading date')
    embedded = []
    for r in t['document_embedding']:
        vector = json.loads(r['embedding']) if isinstance(r['embedding'], str) else r['embedding']
        if r['dimensions'] != 1536 or len(vector) != 1536 or r['embedding_model'] != 'text-embedding-3-small':
            raise ValueError('Embedding model or dimension mismatch')
        if not all(math.isfinite(float(v)) for v in vector):
            raise ValueError('Non-finite vector')
        embedded.append(r['document_chunk_id'])
    if len(embedded) != len(set(embedded)) or set(embedded) != ids['document_chunk'] or not embedded:
        raise ValueError('Embeddings must cover every chunk exactly once')
    if bundle['selection'].get('policy') == 'top10-history-v2':
        if set(bundle.get('historyAudit', {})) != codes:
            raise ValueError('Missing per-instrument history audit')
        listing = bundle['selection'].get('entityStarts', {})
        for security in t['security']:
            if security['ticker'] in listing and min(dates[security['id']]) < listing[security['ticker']]:
                raise ValueError('Predecessor prices are not current-entity history')
    ticker = {r['id']: r['ticker'] for r in t['security']}
    return {'counts': {k: len(v) for k, v in t.items()}, 'commonPriceDates': len(common),
            'commonFrom': common[0], 'asOf': common[-1],
            'missingAdjustedClose': {ticker[k]: v for k, v in adjusted_missing.items()},
            'benchmarkFrom': min((r['trading_date'] for r in t['taiwan_total_return_index']), default=None),
            'benchmarkTo': max((r['trading_date'] for r in t['taiwan_total_return_index']), default=None),
            'embeddingCoverage': 'complete', 'excludedPrices': bundle.get('excludedPrices', []),
            'limitations': ['Fixed constituent snapshot is not historical index membership.',
                           'Existing adjusted prices are preserved; non-null is not a corporate-action audit.',
                           'PDF objects are deliberately absent; live AI is not validated by this seed.']}


def load_bundle(directory):
    path = Path(directory)
    manifest = json.loads((path / 'manifest.json').read_text())
    if manifest['asset'] not in ('equitylens-seed-v1.json.gz', 'equitylens-seed-v2.json.gz'):
        raise ValueError('Unexpected asset name')
    data = (path / manifest['asset']).read_bytes()
    if len(data) != manifest['bytes'] or hashlib.sha256(data).hexdigest() != manifest['sha256']:
        raise ValueError('Seed checksum/size mismatch')
    bundle = json.loads(gzip.decompress(data))
    report = validate(bundle)
    if report != manifest['validation'] or bundle['migrations'] != manifest['migrations'] or bundle['asOf'] != manifest['asOf']:
        raise ValueError('Manifest does not describe this bundle')
    return bundle, manifest


def password_hash(password):
    # ASP.NET Core Identity V3: marker, PRF HMACSHA512=2, iterations, salt length.
    salt = secrets.token_bytes(16)
    return base64.b64encode(b'\x01' + struct.pack('>III', 2, 100000, len(salt)) + salt +
                            hashlib.pbkdf2_hmac('sha512', password.encode(), salt, 100000, 32)).decode()


def demo_rows(bundle, passwords):
    as_of = bundle['asOf']
    now = as_of + 'T12:00:00Z'
    rows = {t: [] for t in PERSONAL}
    for i, password in enumerate(passwords, 1):
        rows['app_user'].append(dict(id=uid(f'user-{i}'), email=f'seed-user-{i}@example.invalid',
            display_name=f'Cloud Seed {i}', password_hash=password_hash(password), role='User',
            is_active=True, created_at_utc=now, updated_at_utc=now))
    securities = {r['ticker']: r['id'] for r in bundle['tables']['security']}
    prices = {}
    for r in bundle['tables']['market_price']:
        prices.setdefault(r['security_id'], {})[r['price_time'][:10]] = r['close']
    definitions = [('etf', 1, '0050 ETF 示範', ['0050']),
                   ('diversified', 1, '台股分散示範', [r['code'] for r in bundle['selection']['constituents']] if bundle['selection'].get('policy') == 'top10-history-v2' else ['2330', '2317', '2454', '2881', '2412', '1216']),
                   ('concentrated', 1, '台積電與鴻海集中示範', ['2330', '2317']),
                   ('empty', 1, '空投組', []), ('owner-check', 2, '第二位使用者的投組', ['0050'])]
    for key, owner, name, tickers in definitions:
        pid = uid('portfolio-' + key)
        rows['portfolio'].append(dict(id=pid, owner_user_id=uid(f'user-{owner}'), name=name,
            description=f'Synthetic ledger; historical prices; as-of {as_of}.', base_currency='TWD',
            is_active=True, created_at_utc=now, updated_at_utc=now))
        if not tickers:
            continue
        common = sorted(set.intersection(*(set(prices[securities[c]]) for c in tickers)))
        # Recent ~120 sessions avoids carrying unmodelled splits through synthetic transactions.
        buy_day, sell_day = common[-120], common[-20]
        deposit = 0
        for c in tickers:
            sid = securities[c]
            price = prices[sid][buy_day]
            budget = 600000 if key == 'concentrated' and c == '2330' else 100000
            qty = max(10, int(budget / price))
            sold = max(1, qty // 10)
            fee = round(qty * price * .001425, 6)
            deposit += qty * price + fee
            for kind, day, quantity, cost, txfee in [('BUY', buy_day, qty, price, fee),
                ('SELL', sell_day, sold, prices[sid][sell_day], round(sold * prices[sid][sell_day] * .001425, 6))]:
                rows['portfolio_transaction'].append(dict(id=uid(f'{key}-{c}-{kind}'), portfolio_id=pid,
                    security_id=sid, transaction_type=kind, quantity=quantity, price=cost, fee=txfee,
                    transaction_date=day, note='Synthetic seed transaction', created_at_utc=now))
            rows['portfolio_holding'].append(dict(id=uid(f'{key}-{c}-holding'), portfolio_id=pid,
                security_id=sid, quantity=qty-sold, average_cost=round((qty * price + fee) / qty, 6),
                cost_currency='TWD', note='Synthetic seed holding', updated_at_utc=now))
        for kind, day, amount in [('Deposit', buy_day, round(deposit + 50000, 6)), ('Withdrawal', as_of, 10000)]:
            rows['portfolio_cash_flow'].append(dict(id=uid(f'{key}-{kind}'), portfolio_id=pid,
                flow_type=kind, amount=amount, currency='TWD', effective_date=day, status='Confirmed',
                is_user_adjusted=True, is_system_derived=False, note='Synthetic seed cash flow', created_at_utc=now))
    return rows


def target_guard(container, database):
    if not database or not re.fullmatch(r'equitylens_[a-z0-9_]*seed', database):
        raise ValueError('Target database must be named equitylens_*seed')
    if not re.fullmatch(r'equitylens-seed-[a-z0-9-]+', container):
        raise ValueError('Target container must be named equitylens-seed-*')
    labels = json.loads(run(['docker', 'inspect', '--format', '{{json .Config.Labels}}', container])) or {}
    if labels.get('equitylens.seed') != 'true':
        raise ValueError('Target container must have equitylens.seed=true label')


def insert_rows(table, rows):
    if table not in TABLES + PERSONAL:
        raise ValueError('Table not allowed')
    statements = []
    for offset in range(0, len(rows), 500):
        batch = rows[offset:offset+500]
        columns = sorted(batch[0])
        if any(sorted(r) != columns for r in batch) or any(not re.fullmatch(r'[a-z][a-z0-9_]*', c) for c in columns):
            raise ValueError('Inconsistent or invalid column names')
        cols = ','.join('"' + c + '"' for c in columns)
        statements.append(f'INSERT INTO {table} ({cols}) SELECT {cols} FROM jsonb_populate_recordset(NULL::{table}, {json_sql(batch)});')
    return statements


def import_bundle(args):
    bundle, manifest = load_bundle(args.directory)
    target_guard(args.container, args.database)
    credentials = Path(args.credentials)
    marker_sql = "SELECT COALESCE(to_regclass('public.cloud_seed_receipt')::text,'');"
    marker = psql(args.container, args.database, marker_sql).strip()
    if marker:
        receipt = psql(args.container, args.database, 'SELECT sha256 FROM cloud_seed_receipt;').strip()
        if receipt != manifest['sha256']:
            raise ValueError('Different seed already imported; use a fresh isolated database')
        if not credentials.exists():
            raise ValueError('Already imported but credentials are missing; do not silently rotate passwords')
        verify_database(args.container, args.database, bundle)
        print('Already imported; database verification passed. Credentials unchanged.')
        return
    if credentials.exists():
        raise ValueError('Credentials file exists without database receipt; move it aside before retrying')
    actual = psql(args.container, args.database, 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1;').splitlines()
    if not set(bundle['migrations']).issubset(set(actual)):
        raise ValueError('Apply all required EF migrations before importing')
    passwords = [secrets.token_urlsafe(30), secrets.token_urlsafe(30)]
    demo = demo_rows(bundle, passwords)
    sql = ['BEGIN;', "SET LOCAL lock_timeout='5s';", "SELECT pg_advisory_xact_lock(41993201);"]
    sql += [f'LOCK TABLE {t} IN EXCLUSIVE MODE;' for t in TABLES + PERSONAL]
    empty = ' OR '.join(f'EXISTS(SELECT 1 FROM {t})' for t in TABLES + PERSONAL)
    sql += [f"DO $$ BEGIN IF {empty} THEN RAISE EXCEPTION 'Seed requires empty domain tables'; END IF; END $$;"]
    sql += insert_rows('app_user', demo.pop('app_user'))
    for table in TABLES:
        rows = [dict(r) for r in bundle['tables'][table]]
        if table in ('uploaded_file', 'financial_filing'):
            for r in rows:
                r['uploaded_by_user_id'] = uid('user-1')
        sql += insert_rows(table, rows)
    for table, rows in demo.items():
        sql += insert_rows(table, rows)
    sql += ['CREATE TABLE cloud_seed_receipt (sha256 text PRIMARY KEY, as_of date NOT NULL);',
            f"INSERT INTO cloud_seed_receipt VALUES ({literal(manifest['sha256'])},{literal(bundle['asOf'])});", 'COMMIT;']
    # Create credentials with exclusive mode/0600 before commit, never print passwords.
    credentials.parent.mkdir(parents=True, exist_ok=True)
    fd = os.open(credentials, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'w') as f:
        json.dump({'asOf': bundle['asOf'], 'accounts': [dict(email=f'seed-user-{i}@example.invalid', password=p)
                  for i, p in enumerate(passwords, 1)]}, f, indent=2)
    try:
        psql(args.container, args.database, '\n'.join(sql), readonly=False)
    except Exception:
        # Keep file: uncertain commit outcomes must not lose the generated credentials.
        raise RuntimeError('Import failed; credentials retained privately. Check receipt before retrying.') from None
    verify_database(args.container, args.database, bundle)
    print(f'Imported and verified. Private login credentials: {credentials}')


def verify_database(container, database, bundle):
    queries = [f"SELECT jsonb_build_object('table',{literal(t)},'count',count(*)) FROM {t};" for t in TABLES]
    queries += ["SELECT jsonb_build_object('ledger_mismatches',count(*)) FROM portfolio_holding h WHERE h.quantity <> COALESCE((SELECT sum(CASE WHEN t.transaction_type='BUY' THEN t.quantity ELSE -t.quantity END) FROM portfolio_transaction t WHERE t.portfolio_id=h.portfolio_id AND t.security_id=h.security_id),0);",
                "SELECT jsonb_build_object('users',count(*),'ordinary',count(*) FILTER (WHERE role='User')) FROM app_user;",
                "SELECT jsonb_build_object('portfolios',count(*),'owners',count(DISTINCT owner_user_id)) FROM portfolio;"]
    results = [json.loads(s) for s in psql(container, database, '\n'.join(queries)).splitlines()]
    for r in results:
        if 'table' in r and r['count'] != len(bundle['tables'][r['table']]):
            raise ValueError(f"Imported count mismatch: {r['table']}")
        if r.get('ledger_mismatches', 0) != 0:
            raise ValueError('Transaction/holding mismatch')
        if 'users' in r and (r['users'], r['ordinary']) != (2, 2):
            raise ValueError('Unexpected users or privileges')
        if 'portfolios' in r and (r['portfolios'], r['owners']) != (5, 2):
            raise ValueError('Unexpected portfolio ownership')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    p = sub.add_parser('export')
    p.add_argument('--container', required=True)
    p.add_argument('--database')
    p.add_argument('--constituents', default=str(ROOT / 'data/seed/constituents-2026-11-04.json'))
    p.add_argument('--output', required=True)
    p = sub.add_parser('validate')
    p.add_argument('--directory', required=True)
    p = sub.add_parser('import')
    p.add_argument('--container', required=True)
    p.add_argument('--database', required=True)
    p.add_argument('--directory', required=True)
    p.add_argument('--credentials', required=True)
    args = parser.parse_args()
    if args.command == 'export':
        export(args)
    elif args.command == 'validate':
        bundle, _ = load_bundle(args.directory)
        print(json.dumps(validate(bundle), ensure_ascii=False, indent=2))
    else:
        import_bundle(args)


if __name__ == '__main__':
    try:
        main()
    except (ValueError, RuntimeError) as exc:
        sys.exit(str(exc))
