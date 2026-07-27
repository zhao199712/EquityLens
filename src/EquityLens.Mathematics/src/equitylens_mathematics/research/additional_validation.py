"""Additional robustness validation over the immutable Phase-5 daily forecasts.

This module implements Tasks 1-3 without changing model formulas or input data.
The MCS implementation follows Hansen, Lunde and Nason (2011), using the Tmax
statistic and sequential elimination of the model with the largest average
loss relative to all surviving alternatives. Circular block bootstrap samples
the full loss matrix jointly, preserving cross-model and serial dependence.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from scipy.stats import chi2

MODELS = (
    "MVEWMA-FHS",
    "GARCH-t + Joint-Vector FHS",
    "GJR-GARCH-t + Joint-Vector FHS",
    "GARCH/GJR 50-50 CDF Ensemble",
)
SHORT = {
    MODELS[0]: "MVEWMA",
    MODELS[1]: "VT-GARCH",
    MODELS[2]: "VT-GJR",
    MODELS[3]: "Ensemble",
}
CONFIDENCES = (0.95, 0.99)
BLOCK_LENGTH = 21
REPETITIONS = 10_000


def qloss(actual: np.ndarray, var: np.ndarray, confidence: float) -> np.ndarray:
    q = 1.0 - confidence
    return (q - (actual < var).astype(float)) * (actual - var)


def kupiec(breaches: np.ndarray, expected: float) -> float:
    n = len(breaches)
    x = int(breaches.sum())
    if x in (0, n):
        return 0.0
    phat = x / n
    lr = -2.0 * (
        (n - x) * math.log((1 - expected) / (1 - phat))
        + x * math.log(expected / phat)
    )
    return float(chi2.sf(max(0.0, lr), 1))


def christoffersen(breaches: np.ndarray) -> float:
    n00 = n01 = n10 = n11 = 0
    for left, right in zip(breaches[:-1], breaches[1:]):
        if not left and not right:
            n00 += 1
        elif not left and right:
            n01 += 1
        elif left and not right:
            n10 += 1
        else:
            n11 += 1
    pi = (n01 + n11) / max(1, n00 + n01 + n10 + n11)
    pi0 = n01 / max(1, n00 + n01)
    pi1 = n11 / max(1, n10 + n11)
    eps = np.finfo(float).tiny
    null = (n00 + n10) * math.log(max(1 - pi, eps)) + (
        n01 + n11
    ) * math.log(max(pi, eps))
    alt = n00 * math.log(max(1 - pi0, eps)) + n01 * math.log(
        max(pi0, eps)
    ) + n10 * math.log(max(1 - pi1, eps)) + n11 * math.log(max(pi1, eps))
    return float(chi2.sf(max(0.0, -2.0 * (null - alt)), 1))


def seed_for(label: str) -> int:
    return int.from_bytes(hashlib.sha256(label.encode()).digest()[:8], "little")


def circular_indices(
    n: int, repetitions: int, block: int, label: str
) -> np.ndarray:
    rng = np.random.default_rng(seed_for(label))
    blocks = math.ceil(n / block)
    starts = rng.integers(0, n, size=(repetitions, blocks))
    offsets = np.arange(block)
    return ((starts[..., None] + offsets) % n).reshape(repetitions, -1)[:, :n]


def read_points(path: Path) -> tuple[str, dict[tuple[str, float], list[dict]]]:
    payload = json.loads(path.read_text())
    points = {
        (row["model"], float(row["confidenceLevel"])): row["points"]
        for row in payload["dailyPoints"]
    }
    return payload["weighting"], points


def point_arrays(points: list[dict], confidence: float) -> dict[str, np.ndarray]:
    actual = np.asarray([p["actualReturn"] for p in points])
    var = np.asarray([p["predictedVaR"] for p in points])
    return {
        "dates": np.asarray([p["date"] for p in points]),
        "actual": actual,
        "var": var,
        "es": np.asarray([p["predictedES"] for p in points]),
        "breach": actual < var,
        "loss": qloss(actual, var, confidence),
    }


def run_rolling(inputs: list[Path], out: Path) -> None:
    rows: list[dict] = []
    cumulative: list[dict] = []
    summary: list[dict] = []
    for path in inputs:
        weighting, points = read_points(path)
        for confidence in CONFIDENCES:
            arrays = {
                model: point_arrays(points[(model, confidence)], confidence)
                for model in MODELS
            }
            delta = arrays[MODELS[1]]["loss"] - arrays[MODELS[0]]["loss"]
            cumulative.extend(
                {
                    "weighting": weighting,
                    "confidence": confidence,
                    "date": date,
                    "daily_delta_ql": value,
                    "cumulative_delta_ql": total,
                }
                for date, value, total in zip(
                    arrays[MODELS[0]]["dates"], delta, np.cumsum(delta)
                )
            )
            winners = []
            for start in range(0, len(delta) - 252 + 1, 63):
                stop = start + 252
                means = {
                    model: float(arrays[model]["loss"][start:stop].mean())
                    for model in MODELS
                }
                ranking = sorted(MODELS, key=means.get)
                d = means[MODELS[1]] - means[MODELS[0]]
                winner = "VT-GARCH" if d < 0 else "MVEWMA" if d > 0 else "tie"
                winners.append((winner, d))
                for model in MODELS:
                    breach = arrays[model]["breach"][start:stop]
                    rows.append(
                        {
                            "weighting": weighting,
                            "confidence": confidence,
                            "window_start": arrays[model]["dates"][start],
                            "window_end": arrays[model]["dates"][stop - 1],
                            "model": SHORT[model],
                            "mean_ql": means[model],
                            "rank": ranking.index(model) + 1,
                            "delta_ql_garch_minus_mvewma": d,
                            "garch_vs_mvewma_winner": winner,
                            "breach_count": int(breach.sum()),
                            "breach_rate": float(breach.mean()),
                            "kupiec_p": kupiec(breach, 1 - confidence),
                            "christoffersen_p": christoffersen(breach),
                            "christoffersen_directional_only": int(breach.sum()) < 8,
                        }
                    )
            values = np.asarray([d for _, d in winners])
            counts = {name: sum(w == name for w, _ in winners) for name in ("VT-GARCH", "MVEWMA", "tie")}
            summary.append(
                {
                    "weighting": weighting,
                    "confidence": confidence,
                    "window_count": len(winners),
                    "garch_win_share": counts["VT-GARCH"] / len(winners),
                    "mvewma_win_share": counts["MVEWMA"] / len(winners),
                    "tie_share": counts["tie"] / len(winners),
                    "delta_median": float(np.median(values)),
                    "delta_q1": float(np.quantile(values, 0.25)),
                    "delta_q3": float(np.quantile(values, 0.75)),
                }
            )
    write_csv(out / "rolling_window_stability.csv", rows)
    write_csv(out / "cumulative_ql_difference.csv", cumulative)
    (out / "rolling_window_summary.json").write_text(
        json.dumps(summary, indent=2, ensure_ascii=False) + "\n"
    )


def ranking_bootstrap(losses: np.ndarray, label: str) -> dict:
    idx = circular_indices(len(losses), REPETITIONS, BLOCK_LENGTH, label)
    means = losses[idx].mean(axis=1)
    ranks = np.argsort(np.argsort(means, axis=1), axis=1) + 1
    diff = means[:, 1] - means[:, 0]
    parent = np.minimum(means[:, 1], means[:, 2])
    result = {
        "seed": seed_for(label),
        "blockLength": BLOCK_LENGTH,
        "repetitions": REPETITIONS,
        "firstProbability": dict(zip([SHORT[m] for m in MODELS], np.mean(ranks == 1, axis=0).tolist())),
        "topTwoProbability": dict(zip([SHORT[m] for m in MODELS], np.mean(ranks <= 2, axis=0).tolist())),
        "meanRank": dict(zip([SHORT[m] for m in MODELS], ranks.mean(axis=0).tolist())),
        "garchBeatsMvewmaProbability": float(np.mean(diff < 0)),
        "garchMinusMvewma": {
            "mean": float(diff.mean()),
            "median": float(np.median(diff)),
            "ci90": np.quantile(diff, [0.05, 0.95]).tolist(),
            "ci95": np.quantile(diff, [0.025, 0.975]).tolist(),
        },
        "gjrMinusGarchMean": float(np.mean(means[:, 2] - means[:, 1])),
        "ensembleMinusBestParentMean": float(np.mean(means[:, 3] - parent)),
        "fullRankingCounts": {},
    }
    unique, counts = np.unique(ranks, axis=0, return_counts=True)
    result["fullRankingCounts"] = {
        ">".join(SHORT[MODELS[i]] for i in np.argsort(row)): int(count)
        for row, count in zip(unique, counts)
    }
    return result


def mcs(losses: np.ndarray, label: str, alpha: float) -> dict:
    """HLN Tmax MCS with sequential max-average-loss elimination."""
    members = list(range(losses.shape[1]))
    idx = circular_indices(len(losses), REPETITIONS, BLOCK_LENGTH, label)
    rounds = []
    while len(members) > 1:
        current = losses[:, members]
        observed = current.mean(axis=0) - current.mean()
        boot = current[idx].mean(axis=1)
        centered = boot - current.mean(axis=0)
        boot_relative = centered - centered.mean(axis=1, keepdims=True)
        scale = boot_relative.std(axis=0, ddof=1)
        scale = np.where(scale > 0, scale, np.inf)
        statistic = float(np.max(observed / scale))
        boot_stats = np.max(boot_relative / scale, axis=1)
        p = float((1 + np.count_nonzero(boot_stats >= statistic)) / (REPETITIONS + 1))
        eliminated = None
        if p < alpha:
            local = int(np.argmax(observed))
            eliminated = members.pop(local)
        rounds.append(
            {
                "membersBefore": [SHORT[MODELS[i]] for i in (members + ([eliminated] if eliminated is not None else []))],
                "testStatistic": statistic,
                "bootstrapPValue": p,
                "eliminated": SHORT[MODELS[eliminated]] if eliminated is not None else None,
            }
        )
        if eliminated is None:
            break
    return {"confidence": 1 - alpha, "members": [SHORT[MODELS[i]] for i in members], "rounds": rounds}


def run_bootstrap_mcs(inputs: list[Path], out: Path) -> None:
    ranking = {}
    sets = {}
    for path in inputs:
        weighting, points = read_points(path)
        for confidence in CONFIDENCES:
            losses = np.column_stack(
                [point_arrays(points[(m, confidence)], confidence)["loss"] for m in MODELS]
            )
            key = f"{weighting}:{confidence}"
            ranking[key] = ranking_bootstrap(losses, f"ranking:{key}")
            sets[key] = [
                mcs(losses, f"mcs90:{key}", 0.10),
                mcs(losses, f"mcs95:{key}", 0.05),
            ]
    ranking_text = json.dumps(ranking, indent=2, ensure_ascii=False) + "\n"
    ranking["deterministicOutputHash"] = hashlib.sha256(ranking_text.encode()).hexdigest()
    (out / "bootstrap_model_ranking.json").write_text(
        json.dumps(ranking, indent=2, ensure_ascii=False) + "\n"
    )
    (out / "model_confidence_set.json").write_text(
        json.dumps(sets, indent=2, ensure_ascii=False) + "\n"
    )


def paired_test(difference: np.ndarray, label: str) -> dict:
    idx = circular_indices(
        len(difference), REPETITIONS, BLOCK_LENGTH, label
    )
    observed = float(difference.mean())
    sampled = difference[idx].mean(axis=1)
    centered = sampled - observed
    p = float(
        (1 + np.count_nonzero(np.abs(centered) >= abs(observed)))
        / (REPETITIONS + 1)
    )
    return {
        "meanDifference": observed,
        "twoSidedPValue": p,
        "ci90": np.quantile(sampled, [0.05, 0.95]).tolist(),
        "ci95": np.quantile(sampled, [0.025, 0.975]).tolist(),
    }


def fz0_loss(
    actual: np.ndarray, var: np.ndarray, es: np.ndarray, confidence: float
) -> np.ndarray:
    """Fissler-Ziegel zero-homogeneous joint VaR/ES score for left-tail returns.

    ES must be strictly negative. The score is consistent but is not required
    to be non-negative; only finiteness and the domain restriction are tested.
    """
    q = 1.0 - confidence
    if np.any(es >= 0):
        raise ValueError("FZ0 left-tail ES must be strictly negative")
    indicator = (actual <= var).astype(float)
    return (
        -(indicator * (var - actual)) / (q * es)
        + var / es
        + np.log(-es)
        - 1.0
    )


def tail_ratio(
    actual: np.ndarray, var: np.ndarray, es: np.ndarray
) -> tuple[float, int]:
    tail = actual < var
    if not np.any(tail):
        return float("nan"), 0
    return float(abs(actual[tail].mean()) / abs(es[tail].mean())), int(tail.sum())


def bootstrap_tail_ratio(
    actual: np.ndarray, var: np.ndarray, es: np.ndarray, label: str
) -> dict:
    idx = circular_indices(len(actual), REPETITIONS, BLOCK_LENGTH, label)
    sampled_actual = actual[idx]
    sampled_var = var[idx]
    sampled_es = es[idx]
    mask = sampled_actual < sampled_var
    counts = mask.sum(axis=1)
    numerator = np.abs(
        np.divide(
            np.where(mask, sampled_actual, 0.0).sum(axis=1),
            counts,
            out=np.full(REPETITIONS, np.nan),
            where=counts > 0,
        )
    )
    denominator = np.abs(
        np.divide(
            np.where(mask, sampled_es, 0.0).sum(axis=1),
            counts,
            out=np.full(REPETITIONS, np.nan),
            where=counts > 0,
        )
    )
    ratios = numerator / denominator
    valid = ratios[np.isfinite(ratios)]
    return {
        "ci90": np.quantile(valid, [0.05, 0.95]).tolist(),
        "ci95": np.quantile(valid, [0.025, 0.975]).tolist(),
        "validBootstrapRepetitions": len(valid),
    }


def run_multi_confidence(inputs: list[Path], out: Path) -> None:
    metric_rows: list[dict] = []
    violations: list[dict] = []
    es_rows: list[dict] = []
    comparisons: dict[str, dict] = {}
    for path in inputs:
        weighting, points = read_points(path)
        available = sorted(
            {confidence for _, confidence in points}, reverse=False
        )
        by_model: dict[str, dict[float, dict[str, np.ndarray]]] = {}
        for model in MODELS:
            by_model[model] = {
                confidence: point_arrays(points[(model, confidence)], confidence)
                for confidence in available
            }
            for lower, upper in zip(available[:-1], available[1:]):
                lo = by_model[model][lower]
                hi = by_model[model][upper]
                for index in np.flatnonzero(
                    (hi["var"] > lo["var"] + 1e-15)
                    | (hi["es"] > lo["es"] + 1e-15)
                ):
                    violations.append(
                        {
                            "weighting": weighting,
                            "model": SHORT[model],
                            "date": lo["dates"][index],
                            "lower_confidence": lower,
                            "upper_confidence": upper,
                            "lower_var": lo["var"][index],
                            "upper_var": hi["var"][index],
                            "lower_es": lo["es"][index],
                            "upper_es": hi["es"][index],
                            "type": "VaR monotonicity"
                            if hi["var"][index] > lo["var"][index] + 1e-15
                            else "ES monotonicity",
                        }
                    )
            for confidence in available:
                arr = by_model[model][confidence]
                invalid = (
                    ~np.isfinite(arr["actual"])
                    | ~np.isfinite(arr["var"])
                    | ~np.isfinite(arr["es"])
                    | (arr["es"] > arr["var"] + 1e-15)
                )
                for index in np.flatnonzero(invalid):
                    violations.append(
                        {
                            "weighting": weighting,
                            "model": SHORT[model],
                            "date": arr["dates"][index],
                            "lower_confidence": confidence,
                            "upper_confidence": confidence,
                            "lower_var": arr["var"][index],
                            "upper_var": arr["var"][index],
                            "lower_es": arr["es"][index],
                            "upper_es": arr["es"][index],
                            "type": "finite/domain/ES-VaR coherence",
                        }
                    )
                ratio, tail_count = tail_ratio(
                    arr["actual"], arr["var"], arr["es"]
                )
                severity = (
                    float(np.mean(arr["var"][arr["breach"]] - arr["actual"][arr["breach"]]))
                    if tail_count
                    else float("nan")
                )
                metric_rows.append(
                    {
                        "weighting": weighting,
                        "model": SHORT[model],
                        "confidence": confidence,
                        "observations": len(arr["actual"]),
                        "breach_count": tail_count,
                        "breach_rate": float(arr["breach"].mean()),
                        "kupiec_p": kupiec(arr["breach"], 1 - confidence),
                        "christoffersen_p": christoffersen(arr["breach"]),
                        "mean_ql": float(arr["loss"].mean()),
                        "es_tail_loss_ratio": ratio,
                        "breach_severity": severity,
                        "mean_var": float(arr["var"].mean()),
                        "mean_es": float(arr["es"].mean()),
                    }
                )
                joint = fz0_loss(
                    arr["actual"], arr["var"], arr["es"], confidence
                )
                ci = bootstrap_tail_ratio(
                    arr["actual"],
                    arr["var"],
                    arr["es"],
                    f"tail:{weighting}:{model}:{confidence}",
                )
                for date, value in zip(arr["dates"], joint):
                    es_rows.append(
                        {
                            "weighting": weighting,
                            "model": SHORT[model],
                            "confidence": confidence,
                            "date": date,
                            "fz0_joint_loss": value,
                            "tail_observation_count": tail_count,
                            "es_tail_loss_ratio": ratio,
                            "es_ratio_ci90_low": ci["ci90"][0],
                            "es_ratio_ci90_high": ci["ci90"][1],
                            "es_ratio_ci95_low": ci["ci95"][0],
                            "es_ratio_ci95_high": ci["ci95"][1],
                        }
                    )
        for confidence in available:
            losses = {
                model: by_model[model][confidence]["loss"] for model in MODELS
            }
            joint = {
                model: fz0_loss(
                    by_model[model][confidence]["actual"],
                    by_model[model][confidence]["var"],
                    by_model[model][confidence]["es"],
                    confidence,
                )
                for model in MODELS
            }
            best_parent = min((MODELS[1], MODELS[2]), key=lambda m: losses[m].mean())
            key = f"{weighting}:{confidence}"
            comparisons[key] = {}
            for label, first, second in (
                ("garch_vs_mvewma", MODELS[1], MODELS[0]),
                ("gjr_vs_garch", MODELS[2], MODELS[1]),
                ("ensemble_vs_best_parent", MODELS[3], best_parent),
            ):
                comparisons[key][label] = {
                    "first": SHORT[first],
                    "second": SHORT[second],
                    "quantileLoss": paired_test(
                        losses[first] - losses[second], f"ql:{key}:{label}"
                    ),
                    "fz0JointLoss": paired_test(
                        joint[first] - joint[second], f"fz:{key}:{label}"
                    ),
                }
    write_csv(out / "multi_confidence_backtest.csv", metric_rows)
    if violations:
        write_csv(out / "var_es_coherence_violations.csv", violations)
    else:
        (out / "var_es_coherence_violations.csv").write_text(
            "weighting,model,date,lower_confidence,upper_confidence,lower_var,upper_var,lower_es,upper_es,type\n"
        )
    write_csv(out / "es_joint_loss_results.csv", es_rows)
    (out / "multi_confidence_comparisons.json").write_text(
        json.dumps(comparisons, indent=2, ensure_ascii=False) + "\n"
    )


def write_csv(path: Path, rows: list[dict]) -> None:
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--market", type=Path, required=True)
    parser.add_argument("--equal", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--multi-market", type=Path)
    parser.add_argument("--multi-equal", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    inputs = [args.market, args.equal]
    run_rolling(inputs, args.output)
    run_bootstrap_mcs(inputs, args.output)
    if args.multi_market and args.multi_equal:
        run_multi_confidence(
            [args.multi_market, args.multi_equal], args.output
        )


if __name__ == "__main__":
    main()
