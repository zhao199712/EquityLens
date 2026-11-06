"""E3: correct model with KNOWN volatility, iid t(5) innovations, and the production
rolling 252-day bank (overlapping across days) resampled 10,000 times.
The realised return is a fresh draw that then enters the next day's bank, exactly as
in production. No parameter estimation. Tests sizes of the order-statistic rate and
of the exchangeable Du-Escanciano null when banks overlap."""
import json, sys
import numpy as np
from multiprocessing import Pool
from common import evaluate, ForecastSet

T, BANK, SIMS, REPS = 1500, 252, 10_000, 1000

def one(seed):
    rng = np.random.default_rng(10_000 + seed)
    z = rng.standard_t(5, size=BANK + T) * 0.01
    samples = np.empty((T, SIMS), dtype=np.float32)
    for t in range(T):
        bank = z[t:t + BANK]
        samples[t] = np.sort(bank[rng.integers(0, BANK, SIMS)])
    fc = ForecastSet(z[BANK:], samples, [str(i) for i in range(T)])
    return evaluate(fc, REPS, f"e3:{seed}")

if __name__ == "__main__":
    n = int(sys.argv[1]); out = sys.argv[2]
    with Pool(4) as pool:
        res = pool.map(one, range(n))
    json.dump(res, open(out, "w"))
