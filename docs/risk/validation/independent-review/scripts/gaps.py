"""Quantify returns the eval export drops versus production carry-forward."""
import json, sys, math
import numpy as np
d = sys.argv[1]
for name in ("long9", "top10"):
    prices = json.load(open(f"{d}/{name}-prices.json"))
    rows = json.load(open(f"{d}/{name}-returns.json"))
    uni = json.load(open(f"{d}/{name}-universe.json"))
    codes = uni["tickers"]; w = np.array(uni["weights"])
    cal = sorted({p["date"] for p in prices})
    px = {c: {} for c in codes}
    for p in prices:
        px[p["ticker"]][p["date"]] = p["close"]
    # production-like carry-forward inside common window
    start = max(min(k for k, v in px[c].items() if v is not None) for c in codes)
    end = min(max(k for k, v in px[c].items() if v is not None) for c in codes)
    days = [x for x in cal if start <= x <= end]
    # NB: production would build its calendar only from the portfolio's own assets
    days_prod = [x for x in days if any(px[c].get(x) is not None for c in codes)]
    mat = np.empty((len(codes), len(days_prod)))
    for i, c in enumerate(codes):
        prev = None
        for j, x in enumerate(days_prod):
            v = px[c].get(x)
            if v is None:
                v = prev
            mat[i, j] = v if v is not None else np.nan
            prev = v
    simple = mat[:, 1:] / mat[:, :-1] - 1
    keep = {r["date"] for r in rows}
    prod_dates = days_prod[1:]
    dropped = [k for k, x in enumerate(prod_dates) if x not in keep]
    zero_days = int(np.sum(np.any(simple == 0, axis=0)))
    pr_all = {lab: ww @ simple for lab, ww in (("snapshot", w), ("equal", np.full(len(codes), 1/len(codes))))}
    print(f"\n{name}: union calendar days in window={len(days)}, portfolio-calendar days={len(days_prod)}, export rows={len(rows)}, production returns={len(prod_dates)}, dropped by export={len(dropped)}")
    for lab, pr in pr_all.items():
        dr = pr[dropped]
        dr = dr[np.isfinite(dr)]
        print(f"  {lab}: dropped returns: n={dr.size}, min={dr.min():.4f}, max={dr.max():.4f}, #<-3%={np.sum(dr<-0.03)}, #|r|>5%={np.sum(np.abs(dr)>0.05)}")
        ex = np.array([r["returns"] for r in rows]) @ (w if lab=="snapshot" else np.full(len(codes),1/len(codes)))
        print(f"  {lab}: export 1%-quantile={np.quantile(ex,.01):.4f}; worst dropped ranks among export: {[int(np.sum(ex<v)) for v in np.sort(dr)[:5]]}")
    # dropped dates detail (largest by |snapshot|)
    sr = pr_all["snapshot"]
    order = sorted(dropped, key=lambda k: -abs(sr[k]) if np.isfinite(sr[k]) else 0)[:8]
    for k in order:
        miss = [c for c in codes if px[c].get(prod_dates[k]) is None or px[c].get(days_prod[k]) is None]
        print("   ", days_prod[k], "->", prod_dates[k], f"snap={sr[k]:+.4f}", "missing:", miss)
    print("  production days with >=1 asset zero return (carry-forward or genuine):", zero_days)
