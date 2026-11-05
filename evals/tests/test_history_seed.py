from equitylens_eval.history_seed import return_rows, weights


def test_missing_session_does_not_become_a_multiday_return():
    rows=return_rows({'a':{'01':100,'02':None,'03':120,'04':121}},['01','02','03','04'],['a'])
    assert len(rows)==1
    assert rows[0]['previousDate']=='03'


def test_benchmark_does_not_shorten_portfolio_period():
    series={'a':{'01':100,'02':101,'03':102},'etf':{'03':100}}
    assert len(return_rows(series,['01','02','03'],['a']))==2
    assert return_rows(series,['01','02','03'],['etf'])==[]


def test_snapshot_weights_are_normalized_per_universe():
    selection={'constituents':[{'code':'a','weightPercent':57.81},{'code':'b','weightPercent':5.72}]}
    assert abs(sum(weights(selection,['a','b']))-1)<1e-12
    assert weights(selection,['a'])==[1]
    assert weights(selection,['a','b'],equal=True)==[.5,.5]


def test_history_loader_filters_old_seed_before_calendar(monkeypatch):
    import sys
    import types
    from equitylens_eval.history_seed import load_history
    bundle={'selection':{'policy':'top10-history-v2','eval':{}},'tables':{
        'security':[{'id':'s','ticker':'2330'}],
        'market_price':[{'security_id':'s','price_time':d+'T00:00:00Z','adjusted_close':v} for d,v in [('2003-12-31',50),('2004-01-02',100),('2004-01-05',101)]]},
        'historyAudit':{'2330':{'largeReturns':[{'date':'2003-12-31'}]}}}
    monkeypatch.setitem(sys.modules,'seed',types.SimpleNamespace(load_bundle=lambda _: (bundle,{})))
    _,_,series,calendar=load_history('ignored')
    assert calendar==['2004-01-02','2004-01-05']
    assert '2003-12-31' not in series['2330']
    assert return_rows(series,calendar,['2330'])[0]['previousDate']=='2004-01-02'
