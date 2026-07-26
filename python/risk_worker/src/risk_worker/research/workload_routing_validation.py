"""Deterministic in-sample workload-routing validation.

The rule manifest is external and immutable. This module never selects a
threshold from results and never mutates price data.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import csv
import hashlib
import itertools
import json
import math
import time
from collections import defaultdict
from pathlib import Path

import numpy as np

from risk_worker.engine import (
    ewma_covariances,
    filtered_residuals,
    stable_seed,
    stabilize_covariances,
)
from risk_worker.research.additional_validation import (
    christoffersen,
    fz0_loss,
    kupiec,
    paired_test,
    qloss,
)
from risk_worker.research.robustness_experiments import (
    CONFIDENCES,
    EQUAL,
    MARKET,
    SIMULATIONS,
    TICKERS,
    fit_vt,
    read_returns,
    var_es,
)

SECTORS = {
    "2330": "semiconductor", "2454": "semiconductor",
    "2308": "electronics", "2317": "electronics",
    "3711": "semiconductor", "2303": "semiconductor",
    "2383": "electronics", "2891": "financial",
    "2344": "semiconductor", "2345": "communications",
    "2881": "financial", "2882": "financial", "1303": "materials",
    "2382": "electronics", "2887": "financial", "2360": "electronics",
    "3017": "electronics", "2885": "financial",
    "2412": "telecom", "2886": "financial",
}
BREADTHS = (20, 15, 10, 7, 5, 3, 2, 1)
WEIGHT_METHODS = ("equal", "market_renormalized", "inverse_volatility")


def unique_universes(returns: np.ndarray) -> list[dict]:
    correlation = np.corrcoef(returns)
    rng = np.random.default_rng(20260726)
    output = []
    for breadth in BREADTHS:
        combinations = math.comb(20, breadth)
        selected: dict[tuple[int, ...], set[str]] = defaultdict(set)
        if combinations < 100:
            for combo in itertools.combinations(range(20), breadth):
                selected[combo].add("all_legal")
        else:
            while sum("random" in labels for labels in selected.values()) < 100:
                combo = tuple(sorted(rng.choice(20, breadth, replace=False).tolist()))
                selected[combo].add("random")
        top = tuple(sorted(np.argsort(MARKET)[-breadth:].tolist()))
        selected[top].add("top_market_weight")

        sector_order = sorted(
            range(20), key=lambda i: (SECTORS[TICKERS[i]], -MARKET[i], TICKERS[i])
        )
        buckets: dict[str, list[int]] = defaultdict(list)
        for index in sector_order:
            buckets[SECTORS[TICKERS[index]]].append(index)
        diverse = []
        depth = 0
        keys = sorted(buckets)
        while len(diverse) < breadth:
            for key in keys:
                if depth < len(buckets[key]) and len(diverse) < breadth:
                    diverse.append(buckets[key][depth])
            depth += 1
        selected[tuple(sorted(diverse))].add("sector_diverse")

        for label, direction in (("high_correlation", 1), ("low_correlation", -1)):
            pair_values = [
                (correlation[i, j], i, j)
                for i in range(20) for j in range(i + 1, 20)
            ]
            _, first, second = max(pair_values) if direction == 1 else min(pair_values)
            greedy = [first] if breadth == 1 else [first, second]
            while len(greedy) < breadth:
                candidates = [i for i in range(20) if i not in greedy]
                scores = {
                    i: float(np.mean(correlation[i, greedy])) for i in candidates
                }
                greedy.append(
                    max(scores, key=scores.get) if direction == 1
                    else min(scores, key=scores.get)
                )
            selected[tuple(sorted(greedy[:breadth]))].add(label)

        for number, (indices, labels) in enumerate(sorted(selected.items()), 1):
            output.append({
                "universe_id": f"b{breadth:02d}_{number:03d}",
                "breadth": breadth,
                "indices": indices,
                "sampling_labels": "+".join(sorted(labels)),
            })
    return output


def weights_for(indices: tuple[int, ...], returns: np.ndarray) -> dict[str, np.ndarray]:
    result = {}
    equal = np.zeros(20)
    equal[list(indices)] = 1 / len(indices)
    result["equal"] = equal
    market = np.zeros(20)
    market[list(indices)] = MARKET[list(indices)]
    market /= market.sum()
    result["market_renormalized"] = market
    inverse = np.zeros(20)
    inverse[list(indices)] = 1 / np.std(returns[list(indices)], axis=1, ddof=1)
    inverse /= inverse.sum()
    result["inverse_volatility"] = inverse
    return result


def workload_features(
    indices: tuple[int, ...], weights: np.ndarray, returns: np.ndarray
) -> dict:
    subset = returns[list(indices)]
    correlation = np.corrcoef(subset)
    if len(indices) == 1:
        average_correlation, first_pc = 0.0, 1.0
    else:
        average_correlation = float(
            correlation[np.triu_indices(len(indices), 1)].mean()
        )
        eigenvalues = np.linalg.eigvalsh(correlation)
        first_pc = float(eigenvalues[-1] / eigenvalues.sum())
    sector_weights: dict[str, float] = defaultdict(float)
    for index in indices:
        sector_weights[SECTORS[TICKERS[index]]] += float(weights[index])
    portfolio = np.exp(returns.T) @ weights - 1
    hhi = float(np.sum(weights**2))
    return {
        "nominal_asset_count": len(indices),
        "hhi": hhi,
        "effective_assets": 1 / hhi,
        "maximum_weight": float(weights.max()),
        "average_pairwise_correlation": average_correlation,
        "first_pc_explained": first_pc,
        "sector_count": len(sector_weights),
        "maximum_sector_weight": max(sector_weights.values()),
        "annualized_portfolio_volatility": float(np.std(portfolio, ddof=1) * np.sqrt(252)),
        "missing_data_rate": 0.0,
    }


def mve_distribution(
    matrix: np.ndarray, local_weights: np.ndarray, seed: int
) -> np.ndarray:
    covariance = stabilize_covariances(ewma_covariances(matrix, .94), .10)
    lower = np.linalg.cholesky(covariance)
    residual = filtered_residuals(matrix, lower)
    rng = np.random.default_rng(seed)
    sampled = residual[rng.integers(0, len(residual), SIMULATIONS)]
    return np.exp(sampled @ lower[-1].T) @ local_weights - 1


def metric(actual: np.ndarray, var: np.ndarray, es: np.ndarray, confidence: float) -> dict:
    breach = actual < var
    ratio = (
        float(abs(actual[breach].mean()) / abs(es[breach].mean()))
        if breach.any() else float("nan")
    )
    return {
        "breach_count": int(breach.sum()),
        "breach_rate": float(breach.mean()),
        "kupiec_p": kupiec(breach, 1 - confidence),
        "christoffersen_p": christoffersen(breach),
        "mean_ql": float(qloss(actual, var, confidence).mean()),
        "mean_fz0": float(fz0_loss(actual, var, es, confidence).mean()),
        "es_tail_loss_ratio": ratio,
    }


def write_csv(path: Path, rows: list[dict]) -> None:
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def _shard_for(workload_id: str, shard_count: int) -> int:
    return int.from_bytes(
        hashlib.sha256(workload_id.encode()).digest()[:8], "little"
    ) % shard_count


def run(
    input_path: Path,
    output: Path,
    shard_index: int = 0,
    shard_count: int = 1,
    suffix: str = "",
) -> dict:
    dates, returns = read_returns(input_path)
    universes = unique_universes(returns)
    workloads = []
    manifest_rows = []
    for universe in universes:
        for method, weights in weights_for(universe["indices"], returns).items():
            workload_id = f'{universe["universe_id"]}_{method}'
            features = workload_features(universe["indices"], weights, returns)
            workloads.append({
                **universe, "workload_id": workload_id,
                "weight_method": method, "weights": weights, **features,
            })
            for ticker, weight in zip(TICKERS, weights):
                manifest_rows.append({
                    "workload_id": workload_id,
                    "universe_id": universe["universe_id"],
                    "sampling_labels": universe["sampling_labels"],
                    "nominal_asset_count": universe["breadth"],
                    "weight_method": method,
                    "ticker": ticker,
                    "included": int(weight > 0),
                    "weight": weight,
                })
    workloads = [
        workload for workload in workloads
        if _shard_for(workload["workload_id"], shard_count) == shard_index
    ]
    selected_ids = {workload["workload_id"] for workload in workloads}
    manifest_rows = [
        row for row in manifest_rows if row["workload_id"] in selected_ids
    ]
    write_csv(output / f"breadth_universe_manifest{suffix}.csv", manifest_rows)

    state = {}
    for workload in workloads:
        n = returns.shape[1] - 252
        state[workload["workload_id"]] = {
            "actual": np.empty(n),
            "mve_var": {c: np.empty(n) for c in CONFIDENCES},
            "mve_es": {c: np.empty(n) for c in CONFIDENCES},
            "garch_var": {c: np.empty(n) for c in CONFIDENCES},
            "garch_es": {c: np.empty(n) for c in CONFIDENCES},
            "runtime_mve": 0.0, "runtime_garch": 0.0,
        }
    parameters = residual = next_variance = None
    fit_count = failures = warning_count = near_unit = 0
    started_all = time.perf_counter()
    for offset, index in enumerate(range(252, returns.shape[1])):
        if offset % 21 == 0:
            params, columns, forecasts = [], [], []
            for asset in range(20):
                fit_count += 1
                try:
                    p, z, forecast, _ = fit_vt(
                        returns[asset, index - 252:index] * 100
                    )
                except Exception:
                    failures += 1
                    raise
                near_unit += int(p["persistence"] >= .995)
                params.append(p); columns.append(z); forecasts.append(forecast)
            parameters = params
            residual = np.column_stack(columns)
            next_variance = np.asarray(forecasts)
        assert parameters is not None and residual is not None and next_variance is not None
        for workload in workloads:
            indices = workload["indices"]
            weights = workload["weights"]
            local_weights = weights[list(indices)]
            key = workload["workload_id"]
            input_hash = hashlib.sha256(
                returns.tobytes() + weights.tobytes()
            ).hexdigest()
            actual = float(np.exp(returns[:, index]) @ weights - 1)
            state[key]["actual"][offset] = actual
            began = time.perf_counter()
            md = mve_distribution(
                returns[np.ix_(indices, range(index - 252, index))],
                local_weights,
                stable_seed(input_hash, offset, False),
            )
            state[key]["runtime_mve"] += time.perf_counter() - began
            began = time.perf_counter()
            rng = np.random.default_rng(
                stable_seed(f"{input_hash}:garch", offset, False)
            )
            local_residual = residual[:, list(indices)]
            sampled = local_residual[
                rng.integers(0, len(local_residual), SIMULATIONS)
            ]
            gd = (
                np.exp(
                    sampled * (np.sqrt(next_variance[list(indices)]) / 100)
                ) @ local_weights - 1
            )
            state[key]["runtime_garch"] += time.perf_counter() - began
            for confidence in CONFIDENCES:
                mv, me = var_es(md, confidence)
                gv, ge = var_es(gd, confidence)
                state[key]["mve_var"][confidence][offset] = mv
                state[key]["mve_es"][confidence][offset] = me
                state[key]["garch_var"][confidence][offset] = gv
                state[key]["garch_es"][confidence][offset] = ge
        current = returns[:, index] * 100
        residual = np.vstack((residual[1:], current / np.sqrt(next_variance)))
        next_variance = np.asarray([
            p["omega"] + p["alpha"] * current[a] ** 2
            + p["beta"] * next_variance[a]
            for a, p in enumerate(parameters)
        ])

    rows = []
    feature_rows = []
    for workload in workloads:
        key = workload["workload_id"]
        s = state[key]
        feature_rows.append({
            k: v for k, v in workload.items()
            if k not in ("indices", "weights")
        })
        for confidence in CONFIDENCES:
            m = metric(s["actual"], s["mve_var"][confidence], s["mve_es"][confidence], confidence)
            g = metric(s["actual"], s["garch_var"][confidence], s["garch_es"][confidence], confidence)
            delta_ql = qloss(s["actual"], s["garch_var"][confidence], confidence) - qloss(
                s["actual"], s["mve_var"][confidence], confidence
            )
            delta_fz = fz0_loss(
                s["actual"], s["garch_var"][confidence], s["garch_es"][confidence], confidence
            ) - fz0_loss(
                s["actual"], s["mve_var"][confidence], s["mve_es"][confidence], confidence
            )
            qtest = paired_test(delta_ql, f"breadth:ql:{key}:{confidence}")
            ftest = paired_test(delta_fz, f"breadth:fz:{key}:{confidence}")
            rows.append({
                **{k: v for k, v in workload.items() if k not in ("indices", "weights")},
                "confidence": confidence,
                **{f"mvewma_{k}": v for k, v in m.items()},
                **{f"garch_{k}": v for k, v in g.items()},
                "delta_ql": qtest["meanDifference"],
                "delta_ql_p": qtest["twoSidedPValue"],
                "delta_ql_ci90_low": qtest["ci90"][0],
                "delta_ql_ci90_high": qtest["ci90"][1],
                "delta_fz0": ftest["meanDifference"],
                "delta_fz0_p": ftest["twoSidedPValue"],
                "delta_fz0_ci90_low": ftest["ci90"][0],
                "delta_fz0_ci90_high": ftest["ci90"][1],
                "runtime_mvewma_seconds": s["runtime_mve"],
                "runtime_garch_seconds": s["runtime_garch"],
                "fit_count_shared": fit_count,
                "near_unit_rate_shared": near_unit / fit_count,
                "convergence_failures": failures,
                "warnings": warning_count,
            })
    write_csv(output / f"workload_feature_dataset{suffix}.csv", feature_rows)
    write_csv(output / f"breadth_validation_results{suffix}.csv", rows)
    diagnostics = {
        "universeCount": len(universes),
        "workloadCount": len(workloads),
        "resultRows": len(rows),
        "fitCount": fit_count,
        "nearUnitCount": near_unit,
        "nearUnitRate": near_unit / fit_count,
        "convergenceFailures": failures,
        "warnings": warning_count,
        "runtimeSeconds": time.perf_counter() - started_all,
        "outOfSampleDays": returns.shape[1] - 252,
        "shardIndex": shard_index,
        "shardCount": shard_count,
    }
    (output / f"breadth_run_diagnostics{suffix}.json").write_text(
        json.dumps(diagnostics, indent=2) + "\n"
    )
    return diagnostics


def _run_shard(arguments: tuple[str, str, int, int]) -> dict:
    input_text, output_text, shard_index, shard_count = arguments
    suffix = f".part-{shard_index:03d}-of-{shard_count:03d}"
    return run(
        Path(input_text), Path(output_text), shard_index, shard_count, suffix
    )


def _merge_csv(parts: list[Path], target: Path, sort_fields: tuple[str, ...]) -> None:
    rows = []
    for part in parts:
        rows.extend(csv.DictReader(part.open()))
    rows.sort(key=lambda row: tuple(row[field] for field in sort_fields))
    write_csv(target, rows)


def parallel_run(
    input_path: Path, output: Path, workers: int, resume: bool
) -> None:
    """Run deterministic workload shards in separate processes.

    Each completed shard is a durable checkpoint. A resumed invocation skips
    shards whose three CSVs and diagnostic JSON already exist. Shards repeat
    the inexpensive 20-asset GARCH fit state, while the dominant per-workload
    EWMA/simulation work scales across CPU cores.
    """
    tasks = []
    for shard in range(workers):
        suffix = f".part-{shard:03d}-of-{workers:03d}"
        expected = [
            output / f"breadth_universe_manifest{suffix}.csv",
            output / f"workload_feature_dataset{suffix}.csv",
            output / f"breadth_validation_results{suffix}.csv",
            output / f"breadth_run_diagnostics{suffix}.json",
        ]
        if resume and all(path.exists() for path in expected):
            continue
        tasks.append((str(input_path), str(output), shard, workers))
    if tasks:
        with concurrent.futures.ProcessPoolExecutor(
            max_workers=workers
        ) as executor:
            list(executor.map(_run_shard, tasks))
    manifest_parts = sorted(output.glob(
        f"breadth_universe_manifest.part-*-of-{workers:03d}.csv"
    ))
    feature_parts = sorted(output.glob(
        f"workload_feature_dataset.part-*-of-{workers:03d}.csv"
    ))
    result_parts = sorted(output.glob(
        f"breadth_validation_results.part-*-of-{workers:03d}.csv"
    ))
    if not (
        len(manifest_parts) == len(feature_parts) == len(result_parts) == workers
    ):
        raise RuntimeError("parallel checkpoint set is incomplete")
    _merge_csv(
        manifest_parts, output / "breadth_universe_manifest.csv",
        ("universe_id", "weight_method", "ticker"),
    )
    _merge_csv(
        feature_parts, output / "workload_feature_dataset.csv",
        ("universe_id", "weight_method"),
    )
    _merge_csv(
        result_parts, output / "breadth_validation_results.csv",
        ("universe_id", "weight_method", "confidence"),
    )
    diagnostics = [
        json.loads(path.read_text())
        for path in sorted(output.glob(
            f"breadth_run_diagnostics.part-*-of-{workers:03d}.json"
        ))
    ]
    (output / "breadth_run_diagnostics.json").write_text(json.dumps({
        "parallelWorkers": workers,
        "checkpointResumeEnabled": resume,
        "workloadCount": sum(row["workloadCount"] for row in diagnostics),
        "resultRows": sum(row["resultRows"] for row in diagnostics),
        "fitCountAcrossWorkers": sum(row["fitCount"] for row in diagnostics),
        "nearUnitRate": max(row["nearUnitRate"] for row in diagnostics),
        "convergenceFailures": sum(row["convergenceFailures"] for row in diagnostics),
        "warnings": sum(row["warnings"] for row in diagnostics),
        "wallRuntimeApproximationSeconds": max(
            row["runtimeSeconds"] for row in diagnostics
        ),
        "sumWorkerRuntimeSeconds": sum(row["runtimeSeconds"] for row in diagnostics),
        "outOfSampleDays": diagnostics[0]["outOfSampleDays"],
    }, indent=2) + "\n")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--workers", type=int, default=1)
    parser.add_argument("--resume", action="store_true")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    if args.workers > 1:
        parallel_run(args.input, args.output, args.workers, args.resume)
    else:
        run(args.input, args.output)


if __name__ == "__main__":
    main()
