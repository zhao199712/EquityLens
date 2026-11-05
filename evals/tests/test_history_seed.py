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
