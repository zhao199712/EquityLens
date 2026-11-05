#!/usr/bin/env python3
"""Fetch maximum Yahoo history and build a reproducible seed from an existing sanitized bundle.
Yahoo Close is split-adjusted, NOT the exchange's unadjusted close. No DB writes.
"""
import argparse
import copy
import csv
import datetime as dt
import gzip
import hashlib
import json
import math
from pathlib import Path
from seed import ROOT, load_bundle, read_selection, uid, validate


def download(directory, selection, end):
    import yfinance as yf
    directory.mkdir(parents=True, exist_ok=True)
    yf.set_tz_cache_location(str(directory / 'cache'))
    symbols = [c + '.TW' if c != '^TWII' else c for c in selection['selected']]
    for symbol in symbols:
        frame = yf.Ticker(symbol).history(start='1900-01-01', end=end,
            auto_adjust=False, actions=True, repair=False, keepna=True, timeout=30)
        if frame.empty:
            raise ValueError('No Yahoo data: ' + symbol)
        frame.index = frame.index.strftime('%Y-%m-%d')
        frame.index.name = 'date'
        frame.to_csv(directory / (symbol.replace('^', '') + '.csv'))
    (directory / 'source.json').write_text(json.dumps(dict(provider='Yahoo Finance via yfinance',
        yfinanceVersion=yf.__version__, fetchedAt=dt.datetime.now(dt.timezone.utc).isoformat(),
        start='1900-01-01', endExclusive=end, autoAdjust=False, repair=False, symbols=symbols), indent=2))


def number(row, key):
    try:
        value = float(row[key])
        return value if math.isfinite(value) else None
    except (ValueError, KeyError, TypeError):
        return None


def clean_history(code, rows, entity_start=None):
    good, excluded, anomalies = [], [], []
    seen = set()
    for row in rows:
        day = row['date']
        dt.date.fromisoformat(day)
        if day in seen:
            raise ValueError('Duplicate Yahoo date: ' + code + ' ' + day)
        seen.add(day)
        prices = [number(row, k) for k in ('Open', 'High', 'Low', 'Close')]
        adj = number(row, 'Close' if code == '^TWII' else 'Adj Close')
        reason = None
        if entity_start and day < entity_start:
            reason = 'before_current_entity_listing'
        elif any(v is None or v <= 0 for v in prices) or adj is None or adj <= 0:
            reason = 'missing_or_nonpositive_price'
        if reason:
            excluded.append({'date': day, 'reason': reason})
            continue
        op, hi, lo, close = prices
        if hi + 1e-5 < max(op, lo, close) or lo - 1e-5 > min(op, hi, close):
            raise ValueError('Inconsistent OHLC: ' + code + ' ' + day)
        if good:
            change = adj / good[-1]['adjusted_close'] - 1
            if abs(change) > .25:
                anomalies.append({'date': day, 'previousDate': good[-1]['date'], 'simpleReturn': change})
        volume = number(row, 'Volume')
        good.append(dict(date=day, open=op, high=hi, low=lo, close=close,
                         adjusted_close=adj, volume=int(volume) if volume is not None else None))
    if not good:
        raise ValueError('No valid prices for ' + code)
    return good, excluded, anomalies


def build(base, snapshots, output, selection_path):
    bundle, base_manifest = load_bundle(base)
    bundle = copy.deepcopy(bundle)
    selection = read_selection(selection_path)
    bundle['selection'] = selection
    source = json.loads((snapshots / 'source.json').read_text())
    source['baseSeedSha256'] = base_manifest['sha256']
    old = {r['ticker']: r for r in bundle['tables']['security']}
    securities, prices, audit = [], [], {}
    for code in selection['selected']:
        symbol = code if code == '^TWII' else code + '.TW'
        path = snapshots / (symbol.replace('^', '') + '.csv')
        with path.open() as f:
            rows = list(csv.DictReader(f))
        good, excluded, anomalies = clean_history(code, rows, selection['entityStarts'].get(code))
        if any(r['date'] >= source['endExclusive'] for r in good):
            raise ValueError('Source contains prices outside frozen end date')
        security = copy.deepcopy(old.get(code))
        if security is None:
            if code != '^TWII':
                raise ValueError('Missing security metadata: ' + code)
            security = dict(id=uid('security-^TWII'), ticker=code, exchange='TWSE', name='臺灣加權股價指數',
                asset_type='Index', currency='TWD', isin=None, sector=None, industry=None, is_active=True,
                metadata_updated_at_utc=source['fetchedAt'], metadata_source='Yahoo Finance',
                prices_synced_at_utc=source['fetchedAt'], prices_source='Yahoo Finance')
        security.update(prices_source='Yahoo split-adjusted', prices_synced_at_utc=source['fetchedAt'])
        securities.append(security)
        for row in good:
            row = dict(row)
            day = row.pop('date')
            prices.append(dict(id=uid('history-v2-' + code + '-' + day), security_id=security['id'],
                price_time=day+'T00:00:00Z', interval='1d', **row,
                data_source='Yahoo price index' if code == '^TWII' else 'Yahoo split OHLC + dividend adj',
                created_at_utc=source['fetchedAt'], updated_at_utc=source['fetchedAt']))
        audit[code] = dict(providerFirst=rows[0]['date'], providerLast=rows[-1]['date'],
            sourceSha256=hashlib.sha256(path.read_bytes()).hexdigest(), acceptedFirst=good[0]['date'],
            acceptedLast=good[-1]['date'], acceptedRows=len(good), excluded=excluded,
            largeReturns=anomalies, corporateActionAudit='not independently reconciled')
    as_of = min(a['acceptedLast'] for a in audit.values())
    bundle.update(asOf=as_of, historySource=source, historyAudit=audit)
    bundle['excludedPrices'] = [{'ticker':c, **r} for c,a in audit.items() for r in a['excluded'] if r['reason']!='before_current_entity_listing']
    bundle['tables']['security'] = securities
    bundle['tables']['market_price'] = [r for r in prices if r['price_time'][:10] <= as_of]
    # Keep the separately named total-return index. ^TWII is never inserted into it.
    report = validate(bundle)
    output.mkdir(parents=True, exist_ok=True)
    name = 'equitylens-seed-v2.json.gz'
    payload = gzip.compress(json.dumps(bundle, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode(), mtime=0)
    (output / name).write_bytes(payload)
    manifest = dict(formatVersion=1, asset=name, sha256=hashlib.sha256(payload).hexdigest(), bytes=len(payload),
        asOf=as_of, selection=selection, migrations=bundle['migrations'], validation=report,
        historySource=source, historyCoverage={c:{k:v for k,v in a.items() if k not in ('excluded','largeReturns')} for c,a in audit.items()},
        pdfIncluded=False, exchangeRatesIncluded=False, requiresPrivateDistribution=True)
    (output/'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n')
    print(json.dumps({'asOf':as_of,'prices':len(prices),'coverage':manifest['historyCoverage'],
                      'largeReturns':{c:len(a['largeReturns']) for c,a in audit.items()}}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--base', type=Path, required=True)
    p.add_argument('--snapshots', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--selection', type=Path, default=ROOT/'data/seed/top10-history.json')
    p.add_argument('--download', action='store_true')
    p.add_argument('--end-exclusive', default=str(dt.date.today()))
    a = p.parse_args()
    if a.download:
        download(a.snapshots, read_selection(a.selection), a.end_exclusive)
    build(a.base, a.snapshots, a.output, a.selection)
