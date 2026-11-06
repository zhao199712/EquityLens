"""E4: null distribution of breach counts and U_ES mean-H for the production rolling
252-scenario bank (known volatility, iid t(5)), at the study lengths T=5375 and 1817.
Compares with the binomial / independent-day nulls used by the report."""
import json, sys, math
import numpy as np
from multiprocessing import Pool
from equitylens_mathematics.engine import historical_var
LEVELS = (0.95, 0.975, 0.99); BANK, SIMS = 252, 10_000

def one(args):
    T, seed = args
    rng = np.random.default_rng(40_000 + seed + 7 * T)
    z = rng.standard_t(5, size=BANK + T)
    breaches = np.zeros(3, int); hsum = np.zeros(3)
    for t in range(T):
        s = np.sort(z[t:t + BANK][rng.integers(0, BANK, SIMS)])
        a = z[BANK + t]
        pit = np.searchsorted(s, a, side="right") / SIMS
        for i, c in enumerate(LEVELS):
            breaches[i] += a < historical_var(s, c)
            tail = 1 - c
            hsum[i] += (tail - pit) / tail if pit <= tail else 0.0
    return T, breaches.tolist(), (hsum / T).tolist()

if __name__ == "__main__":
    reps = int(sys.argv[1])
    jobs = [(5375, s) for s in range(reps)] + [(1817, s) for s in range(reps)]
    with Pool(4) as pool:
        res = pool.map(one, jobs, chunksize=4)
    json.dump(res, open(sys.argv[2], "w"))
