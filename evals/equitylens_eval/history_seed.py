"""Offline historical eval inputs shared by risk research and portfolio AI eval.
No raw-close fallback, interpolation, or returns spanning a missing session.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

REPO = Path(__file__).resolve().parents[2]
DATA_START = '2004-01-01'


def load_history(directory):
    sys.path.insert(0, str(REPO/'scripts/seed'))
    from seed import load_bundle
    bundle, manifest = load_bundle(directory)
    if bundle['selection'].get('policy') != 'top10-history-v2':
        raise ValueError('Expected top10-history-v2 seed')
    by_id = {r['id']:r['ticker'] for r in bundle['tables']['security']}
    series = {c:{} for c in by_id.values()}
    # Keep source missing sessions in the calendar, but discard all-source blank
    # dates. At least one accepted asset price establishes a session.
    for r in bundle['tables']['market_price']:
        series[by_id[r['security_id']]][r['price_time'][:10]] = r['adjusted_close']
    for code, audit in bundle['historyAudit'].items():
        for item in audit['largeReturns']:
            series[code][item['date']] = None
    data_start = max(DATA_START, bundle['selection']['eval'].get('dataStart', DATA_START))
    series = {c:{d:v for d,v in values.items() if d >= data_start} for c,values in series.items()}
    calendar = sorted(set().union(*(set(s) for s in series.values())))
    return bundle, manifest, series, calendar


def return_rows(series, calendar, codes):
    result = []
    for previous, day in zip(calendar, calendar[1:]):
        values = [series[c].get(d) for c in codes for d in (previous, day)]
        if any(v is None or not math.isfinite(v) or v <= 0 for v in values):
            continue
        result.append({'date':day, 'previousDate':previous,
            'returns':[series[c][day]/series[c][previous]-1 for c in codes]})
    return result


def weights(selection, codes, equal=False):
    raw = {r['code']:r['weightPercent'] for r in selection['constituents']}
    values = [1 if equal else raw[c] for c in codes]
    total = sum(values)
    return [v/total for v in values]


def historical_fixture(directory, kind, as_of):
    bundle, manifest, series, calendar = load_history(directory)
    if as_of != bundle['asOf']:
        raise ValueError('Historical eval as-of must equal the frozen seed as-of')
    codes = ['0050'] if kind == 'index_etf' else bundle['selection']['eval']['portfolios']['top10']
    count = 61 if kind == 'short' else 253
    dates = [d for d in calendar if d <= as_of][-count:]
    if len(dates) != count or any(series[c].get(d) is None for c in codes for d in dates):
        raise ValueError('Recent contiguous historical window is incomplete; do not compress gaps')
    allocation = [1.0] if kind == 'index_etf' else weights(bundle['selection'], codes, equal=kind=='balanced')
    names = {r['ticker']:r['name'] for r in bundle['tables']['security']}
    # Index each adjusted series to 100; these are synthetic holdings over real
    # adjusted returns, not historical executable prices or transactions.
    assets = []
    for c,w in zip(codes,allocation):
        values = [100*series[c][d]/series[c][dates[0]] for d in dates]
        assets.append(dict(ticker=c, name=names[c], prices=values,
            quantity=round(w*100000/values[-1],6)))
    return dict(dates=dates,assets=assets,cash=0,as_of=as_of,version=2,
        seed_sha256=manifest['sha256'],price_basis='indexed Yahoo adjusted returns',
        weight_basis='equal' if kind=='balanced' else 'user snapshot normalized')


def export(directory, output):
    bundle, manifest, series, calendar = load_history(directory)
    selection = bundle['selection']; config = selection['eval']
    output.mkdir(parents=True, exist_ok=True)
    coverage = {}
    for name,codes in {**config['portfolios'], **{c:[c] for c in config['benchmarks']}}.items():
        rows = return_rows(series, calendar, codes)
        lookback = config['lookback']
        coverage[name] = dict(tickers=codes, returnDays=len(rows), firstReturn=rows[0]['date'],
            lastReturn=rows[-1]['date'], lookback=lookback, outOfSampleDays=max(0,len(rows)-lookback),
            firstOutOfSample=rows[lookback]['date'] if len(rows)>lookback else None)
        (output/(name.replace('^','')+'-returns.json')).write_text(json.dumps(rows,allow_nan=False))
        if name in config['portfolios']:
            # Compatible with robustness_experiments.read_returns; null rows keep
            # missing sessions visible, preventing multi-day gap bridging.
            payload = [{'date':d,'ticker':c,'close':series[c].get(d)} for d in calendar for c in codes]
            (output/(name+'-prices.json')).write_text(json.dumps(payload,allow_nan=False))
            (output/(name+'-universe.json')).write_text(json.dumps(dict(tickers=codes,
                weights=weights(selection,codes),seedSha256=manifest['sha256'],confidences=config['confidences'],dataStart=max(DATA_START,config.get('dataStart',DATA_START)))))
    metadata = dict(dataStart=max(DATA_START,config.get('dataStart',DATA_START)),warmupPolicy='252 valid returns after dataStart',seedSha256=manifest['sha256'],asOf=bundle['asOf'],coverage=coverage,
        weightPercentSum=sum(r['weightPercent'] for r in selection['constituents']),
        quarantinedReturns={c:[r for r in a['largeReturns'] if r['date'] >= DATA_START] for c,a in bundle['historyAudit'].items() if any(r['date'] >= DATA_START for r in a['largeReturns'])},
        limitations=['Current selection and fixed snapshot weights have survivorship/look-ahead bias.',
            'long9 excludes 3711; it is not the same portfolio as top10.',
            '0050 is dividend-adjusted; TWII is a price index, not total return.',
            'Provider adjustments are not independently audited; flagged jumps excluded, not repaired.',
            'This export does not run models or establish VaR/ES acceptance.'])
    (output/'history-eval-manifest.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(metadata,ensure_ascii=False,indent=2))


if __name__ == '__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--seed',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();export(a.seed,a.output)
