import json
import numpy as np
import pytest
from equitylens_mathematics.research import robustness_experiments as r


def test_nine_asset_cap_cannot_claim_ten_percent():
    with pytest.raises(ValueError, match='Infeasible'):
        r.cap_weights(np.full(9,1/9),.1)
    assert np.allclose(r.cap_weights(np.asarray([.8]+[.025]*8),1/9),np.full(9,1/9))


def test_new_universe_keeps_null_dates_visible(tmp_path,monkeypatch):
    monkeypatch.setattr(r,'TICKERS',['2330','2454'])
    rows=[{'date':d,'ticker':c,'close':v} for d,v in [('2020-01-02',100),('2020-01-03',None),('2020-01-06',110),('2020-01-07',111)] for c in r.TICKERS]
    p=tmp_path/'prices.json';p.write_text(json.dumps(rows))
    dates,returns=r.read_returns(p)
    assert [str(d) for d in dates]==['2020-01-07']
    assert returns.shape==(2,1)


def test_dynamic_fixtures_are_normalized(monkeypatch):
    monkeypatch.setattr(r,'TICKERS',[str(i) for i in range(10)])
    monkeypatch.setattr(r,'MARKET',np.asarray([.7]+[.3/9]*9))
    monkeypatch.setattr(r,'EQUAL',np.full(10,.1))
    _,weights=r.fixtures(np.random.default_rng(7).normal(size=(10,252)))
    assert weights.shape==(205,10)
    assert np.allclose(weights.sum(axis=1),1)
    assert np.all(weights>=0)
