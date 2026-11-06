import numpy as np
from scipy.stats import binom
from equitylens_mathematics.research.additional_validation import christoffersen, kupiec
from equitylens_mathematics.research.es_backtest import (
    ForecastSet, du_escanciano_exchangeable, du_escanciano_mc)
from equitylens_mathematics.research.attribution import production_order_statistic_rate

LEVELS = (0.95, 0.975, 0.99)
OS = {c: production_order_statistic_rate(252, c) for c in LEVELS}

def binom2(k, n, p):
    return float(min(1.0, 2 * min(binom.cdf(k, n, p), binom.sf(k - 1, n, p))))

def evaluate(fc: ForecastSet, reps: int, label: str, model_own: bool = True) -> dict:
    out = {}
    for c in LEVELS:
        var, _ = fc.var_es(c)
        b = fc.actual < var
        ex = du_escanciano_exchangeable(fc, c, reps, label)
        row = {
            "rate": float(b.mean()), "n": int(b.size),
            "kupiec_nominal": kupiec(b, 1 - c),
            "p_vs_os": binom2(int(b.sum()), b.size, OS[c]),
            "christoffersen": christoffersen(b),
            "ex_u_es": ex["u_es_p_right"], "ex_c1": ex["c_es"]["1"]["p"], "ex_c5": ex["c_es"]["5"]["p"],
        }
        if model_own:
            mo = du_escanciano_mc(fc, c, reps, label)
            row.update(mo_u_es=mo["u_es_p_right"], mo_c1=mo["c_es"]["1"]["p"], mo_c5=mo["c_es"]["5"]["p"])
        out[str(c)] = row
    return out
