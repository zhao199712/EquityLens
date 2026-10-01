"""Guarded, sequential refresh through the existing production import endpoint.

Run only with an explicitly backed-up directory. No credentials are stored here.
An invalid update stops the batch; restore uses the exact captured post-update
state as a concurrency guard, and refuses to overwrite any intervening change.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import time
from pathlib import Path


PSQL = ["docker", "exec", "-i", "equitylens-prod-postgres", "sh", "-lc",
        'exec psql -X -qAt -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1']


def sql(query: str) -> str:
    return subprocess.run(PSQL, input=query, text=True, capture_output=True, check=True).stdout.strip()


def snapshot(security_id: str | None = None) -> dict:
    # IDs originate in the DB, not from arbitrary command-line strings.
    import uuid
    scope = f"s.id='{uuid.UUID(security_id)}'" if security_id else "s.is_active AND s.exchange IN ('TWSE','TPEX')"
    query = f"""BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT json_build_object('capturedAt',now(),
 'securities',(SELECT json_agg(s ORDER BY ticker) FROM security s WHERE {scope}),
 'prices',(SELECT json_agg(p ORDER BY p.security_id,p.interval,p.price_time)
 FROM market_price p JOIN security s ON s.id=p.security_id WHERE {scope}));
COMMIT;"""
    return json.loads(sql(query))


def save(path: Path, value: dict) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, sort_keys=True))
    temporary.replace(path)


def quality(value: dict, cutoff: str) -> dict:
    rows = [p for p in value['prices'] or [] if p['interval'] == '1d']
    dates = [p['price_time'][:10] for p in rows]
    keys = [(p['security_id'], p['interval'], p['price_time']) for p in rows]
    return dict(count=len(rows), latest=max(dates, default=None),
                missing_adjusted=sum(p['adjusted_close'] is None for p in rows),
                nonpositive=sum(p['close'] <= 0 or (p['adjusted_close'] is not None and p['adjusted_close'] <= 0) for p in rows),
                duplicates=len(keys)-len(set(keys)), future=sum(d > cutoff for d in dates))


def restore(before: dict, after: dict) -> None:
    """Restore only this security, after locking and verifying its whole state."""
    if 'price_adjustment_batch_id' in before['securities'][0]:
        raise RuntimeError('Legacy restore cannot restore adjustment versions; use the isolated price-repair workflow or a full database backup.')
    sid = before['securities'][0]['id']
    # Dollar quotes are checked because JSON can contain arbitrary source labels.
    tag = '$equitylens_restore$'
    b = json.dumps(before, ensure_ascii=False)
    a = json.dumps(after, ensure_ascii=False)
    if tag in b or tag in a:
        raise ValueError('unexpected SQL delimiter in snapshot')
    sql(f"""BEGIN;
SET LOCAL lock_timeout='5s';
LOCK TABLE security, market_price IN SHARE ROW EXCLUSIVE MODE;
DO $guard$
DECLARE expected jsonb := {tag}{a}{tag}::jsonb;
BEGIN
 IF (SELECT to_jsonb(s) FROM security s WHERE id='{sid}') IS DISTINCT FROM expected->'securities'->0
 OR (SELECT COALESCE(jsonb_agg(p ORDER BY p.security_id,p.interval,p.price_time),'[]'::jsonb)
     FROM market_price p WHERE security_id='{sid}') IS DISTINCT FROM COALESCE(expected->'prices','[]'::jsonb)
 THEN RAISE EXCEPTION 'Concurrent change detected; refusing rollback'; END IF;
END $guard$;
DELETE FROM market_price WHERE security_id='{sid}';
INSERT INTO market_price SELECT * FROM json_populate_recordset(NULL::market_price, {tag}{b}{tag}::json->'prices');
UPDATE security SET prices_source=({tag}{b}{tag}::json->'securities'->0->>'prices_source'),
 prices_synced_at_utc=({tag}{b}{tag}::json->'securities'->0->>'prices_synced_at_utc')::timestamptz
WHERE id='{sid}';
COMMIT;""")


def refresh(directory: Path, cutoff: str, only: str | None) -> None:
    base = json.loads((directory / 'before.json').read_text())
    securities = sorted(base['securities'], key=lambda s: (s['ticker'] != '2330', s['ticker']))
    if only:
        securities = [s for s in securities if s['ticker'] == only]
        if not securities:
            raise ValueError('ticker absent from backed-up Taiwan universe')
    journal = directory / 'updates.jsonl'
    for security in securities:
        ticker, sid = security['ticker'], security['id']
        previous = snapshot(sid)
        save(directory / f'{ticker}-before.json', previous)
        response_path = directory / f'{ticker}-http.txt'
        started = time.monotonic()
        request = subprocess.run([
            'curl', '--silent', '--show-error', '--max-time', '600',
            '--resolve', 'equitylens.duckdns.org:443:127.0.0.1',
            '-H', 'Content-Type: application/json', '-X', 'POST',
            '--data', json.dumps({'from': '2019-11-01', 'to': cutoff}),
            '-o', str(response_path), '-w', '%{http_code}',
            f'https://equitylens.duckdns.org/api/securities/{sid}/prices/import'
        ], capture_output=True, text=True)
        current = snapshot(sid)
        save(directory / f'{ticker}-after.json', current)
        old_q, new_q = quality(previous, cutoff), quality(current, cutoff)
        invalid = (new_q['count'] < old_q['count'] or new_q['missing_adjusted'] > old_q['missing_adjusted']
                   or new_q['nonpositive'] or new_q['duplicates'] or new_q['future'] > old_q['future'])
        item = dict(ticker=ticker, securityId=sid, http=request.stdout, curlExit=request.returncode,
                    seconds=round(time.monotonic()-started, 2), before=old_q, after=new_q,
                    restored=False)
        if invalid:
            restore(previous, current)
            item['restored'] = True
        with journal.open('a') as handle:
            handle.write(json.dumps(item) + '\n')
        print(json.dumps(item), flush=True)
        if invalid or request.returncode or not request.stdout.startswith('2'):
            raise RuntimeError(f'{ticker}: failed HTTP/data validation; batch stopped')
        if new_q['latest'] != cutoff:
            raise RuntimeError(f'{ticker}: price series still stale; batch stopped')
        time.sleep(1)
    frozen = snapshot()
    save(directory / 'snapshot.json', frozen)
    digest = hashlib.sha256((directory / 'snapshot.json').read_bytes()).hexdigest()
    print(f'Frozen snapshot SHA256: {digest}', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--to', required=True)
    parser.add_argument('--only')
    arguments = parser.parse_args()
    refresh(arguments.directory, arguments.to, arguments.only)
