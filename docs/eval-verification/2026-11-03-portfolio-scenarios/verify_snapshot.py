"""Offline independent arithmetic check of the synthetic browser-run snapshot.
Run: python docs/eval-verification/2026-11-03-portfolio-scenarios/verify_snapshot.py
No API calls; not a market backtest or research-question scoring change.
"""
import json
import math
import statistics
from pathlib import Path

comparison = json.loads(Path(__file__).with_name('synthetic-result.json').read_text())['scenarioComparison']
assets = comparison['snapshot']['assets']
series = [[b / a - 1 for a, b in zip(x['prices'], x['prices'][1:])] for x in assets]
for result in [comparison['baseline'], *comparison['scenarios']]:
    weights = [result['weights'][a['ticker']] for a in assets]
    daily = [sum(w * r for w, r in zip(weights, day)) for day in zip(*series)]
    vol = statistics.stdev(daily) * math.sqrt(252)
    var = sorted(daily)[math.ceil(len(daily) * .05) - 1]
    es = statistics.mean(r for r in daily if r <= var)
    value = peak = 1.
    drawdown = 0.
    for r in daily:
        value *= 1 + r
        peak = max(peak, value)
        drawdown = min(drawdown, value / peak - 1)
    expected = dict(annualizedVolatility=vol, historicalVaR=var, expectedShortfall=es, hypotheticalMaxDrawdown=drawdown)
    for field, value in expected.items():
        assert math.isclose(value, result['metrics'][field], abs_tol=1e-8), (result['name'], field)
    assert math.isclose(sum(weights) + result['cashWeight'], 1, abs_tol=1e-12)
    assert math.isclose(sum(c['componentVolatility'] for c in result['metrics']['contributions']), vol, abs_tol=1e-8)
    for a in assets:
        if a['ticker'] != '2327':
            assert result['quantities'][a['ticker']] == a['quantity']
    print(f"PASS {result['name']}: volatility={vol:.8f}, VaR={var:.8f}, ES={es:.8f}, drawdown={drawdown:.8f}")
