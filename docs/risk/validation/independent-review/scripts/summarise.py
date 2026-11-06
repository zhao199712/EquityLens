"""Rejection rates (p<0.05) and pooled breach rates from E2/E3 outputs."""
import json, sys, math
import numpy as np
from scipy.stats import binomtest
for path in sys.argv[1:]:
    res = json.load(open(path))
    groups = {}
    for rep in res:
        items = rep.items() if "conc" in rep else [("iid", rep)]
        for lab, d in items:
            for c, row in d.items():
                if c == "fails":
                    continue
                groups.setdefault((lab, c), []).append(row)
    print(f"\n== {path}: {len(res)} replications")
    keys = ["kupiec_nominal", "p_vs_os", "christoffersen", "ex_u_es", "ex_c1", "ex_c5", "mo_u_es", "mo_c1", "mo_c5"]
    print("label level  pooled_rate(95%CI)  OS_rate | " + " ".join(f"{k:>8}" for k in keys))
    from common import OS
    for (lab, c), rows in sorted(groups.items()):
        n = sum(r["n"] for r in rows); k = round(sum(r["rate"] * r["n"] for r in rows))
        ci = binomtest(k, n).proportion_ci()
        rej = []
        for key in keys:
            ps = np.array([r[key] for r in rows if r.get(key) is not None])
            rej.append(f"{np.mean(ps < 0.05):8.3f}")
        print(f"{lab:5} {c:5}  {k/n*100:.3f}% ({ci.low*100:.2f}-{ci.high*100:.2f})  {OS[float(c)]*100:.2f}% | " + " ".join(rej))
