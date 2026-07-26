"""Cross-workload driver, fixed-strategy and regret analysis."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from collections import defaultdict
from pathlib import Path

import numpy as np

FEATURES = (
    "nominal_asset_count", "hhi", "effective_assets", "maximum_weight",
    "average_pairwise_correlation", "first_pc_explained",
    "annualized_portfolio_volatility", "maximum_sector_weight",
    "missing_data_rate",
)
STRATEGIES = (
    "A_global_mvewma", "B_global_garch", "C_hhi",
    "D_hhi_neff", "E_hhi_first_pc",
)


def read_rows(path: Path) -> list[dict]:
    rows = list(csv.DictReader(path.open()))
    numeric = {
        "nominal_asset_count", "hhi", "effective_assets", "maximum_weight",
        "average_pairwise_correlation", "first_pc_explained", "sector_count",
        "maximum_sector_weight", "annualized_portfolio_volatility",
        "missing_data_rate", "confidence", "mvewma_mean_ql",
        "garch_mean_ql", "mvewma_mean_fz0", "garch_mean_fz0",
        "mvewma_breach_rate", "garch_breach_rate", "mvewma_kupiec_p",
        "garch_kupiec_p", "mvewma_christoffersen_p",
        "garch_christoffersen_p", "mvewma_es_tail_loss_ratio",
        "garch_es_tail_loss_ratio", "runtime_mvewma_seconds",
        "runtime_garch_seconds", "delta_ql", "delta_fz0",
    }
    for row in rows:
        for key in numeric:
            row[key] = float(row[key])
    return rows


def hc1(x: np.ndarray, y: np.ndarray) -> dict:
    beta = np.linalg.lstsq(x, y, rcond=None)[0]
    residual = y - x @ beta
    bread = np.linalg.pinv(x.T @ x)
    meat = x.T @ (residual[:, None] ** 2 * x)
    covariance = len(y) / max(1, len(y) - x.shape[1]) * bread @ meat @ bread
    se = np.sqrt(np.maximum(np.diag(covariance), 0))
    return {"beta": beta, "se": se, "conditionNumber": float(np.linalg.cond(x))}


def regression(rows: list[dict]) -> dict:
    output = {}
    rng = np.random.default_rng(20260726)
    for confidence in (0.95, 0.99):
        subset = [r for r in rows if r["confidence"] == confidence]
        output[str(confidence)] = {}
        raw = np.column_stack([[r[f] for r in subset] for f in FEATURES])
        means, scales = raw.mean(axis=0), raw.std(axis=0, ddof=1)
        active = scales > 1e-12
        active_names = [f for f, use in zip(FEATURES, active) if use]
        standardized = (raw[:, active] - means[active]) / scales[active]
        corr = np.corrcoef(standardized, rowvar=False)
        inv_corr = np.linalg.pinv(corr)
        vif = dict(zip(active_names, np.diag(inv_corr).tolist()))
        for target in ("delta_ql", "delta_fz0"):
            y = np.asarray([r[target] for r in subset])
            target_out = {"univariate": {}, "multivariate": {}}
            for index, name in enumerate(active_names):
                fit = hc1(np.column_stack((np.ones(len(y)), standardized[:, index])), y)
                b, se = fit["beta"][1], fit["se"][1]
                target_out["univariate"][name] = {
                    "standardizedCoefficient": float(b),
                    "hc1StandardError": float(se),
                    "ci90": [float(b - 1.645 * se), float(b + 1.645 * se)],
                    "ci95": [float(b - 1.96 * se), float(b + 1.96 * se)],
                }
            x = np.column_stack((np.ones(len(y)), standardized))
            fit = hc1(x, y)
            stability = np.zeros(len(active_names))
            repetitions = 1000
            for _ in range(repetitions):
                sampled = rng.integers(0, len(y), len(y))
                candidate = hc1(x[sampled], y[sampled])
                stability += (
                    np.abs(candidate["beta"][1:] / np.maximum(candidate["se"][1:], 1e-30))
                    >= 1.645
                )
            for index, name in enumerate(active_names):
                b, se = fit["beta"][index + 1], fit["se"][index + 1]
                target_out["multivariate"][name] = {
                    "standardizedCoefficient": float(b),
                    "hc1StandardError": float(se),
                    "ci90": [float(b - 1.645 * se), float(b + 1.645 * se)],
                    "ci95": [float(b - 1.96 * se), float(b + 1.96 * se)],
                    "bootstrapSelectionStability": float(stability[index] / repetitions),
                    "vif": float(vif[name]),
                }
            target_out["conditionNumber"] = fit["conditionNumber"]
            output[str(confidence)][target] = target_out
    return output


def selected_model(strategy: str, row: dict) -> str:
    if strategy == "A_global_mvewma":
        return "MVEWMA"
    if strategy == "B_global_garch":
        return "VT-GARCH"
    if strategy == "C_hhi":
        return "VT-GARCH" if row["hhi"] < .10 else "MVEWMA"
    if strategy == "D_hhi_neff":
        return (
            "VT-GARCH"
            if row["hhi"] < .10 and row["effective_assets"] >= 10
            else "MVEWMA"
        )
    if strategy == "E_hhi_first_pc":
        return (
            "VT-GARCH"
            if row["hhi"] < .10 and row["first_pc_explained"] < .60
            else "MVEWMA"
        )
    raise ValueError(strategy)


def cluster_test(values: dict[str, list[float]], label: str) -> dict:
    clusters = sorted(values)
    observed = float(np.mean([v for key in clusters for v in values[key]]))
    rng = np.random.default_rng(
        int.from_bytes(hashlib.sha256(label.encode()).digest()[:8], "little")
    )
    samples = np.empty(10_000)
    for repetition in range(10_000):
        selected = rng.choice(clusters, len(clusters), replace=True)
        samples[repetition] = np.mean([v for key in selected for v in values[key]])
    centered = samples - observed
    p = float((1 + np.sum(np.abs(centered) >= abs(observed))) / 10001)
    return {
        "effect": observed,
        "twoSidedClusterBootstrapP": p,
        "ci90": np.quantile(samples, [.05, .95]).tolist(),
        "ci95": np.quantile(samples, [.025, .975]).tolist(),
    }


def analyze_strategies(rows: list[dict]) -> tuple[list[dict], list[dict]]:
    comparisons, regrets = [], []
    for confidence in (0.95, 0.99):
        subset = [r for r in rows if r["confidence"] == confidence]
        for strategy in STRATEGIES:
            per_workload = []
            qdiff: dict[str, list[float]] = defaultdict(list)
            fdiff: dict[str, list[float]] = defaultdict(list)
            for row in subset:
                model = selected_model(strategy, row)
                prefix = "garch" if model == "VT-GARCH" else "mvewma"
                ql = row[f"{prefix}_mean_ql"]
                fz = row[f"{prefix}_mean_fz0"]
                oracle_ql = min(row["mvewma_mean_ql"], row["garch_mean_ql"])
                oracle_fz = min(row["mvewma_mean_fz0"], row["garch_mean_fz0"])
                per_workload.append({
                    "row": row, "model": model, "ql": ql, "fz": fz,
                    "qlRegret": ql - oracle_ql, "fzRegret": fz - oracle_fz,
                    "coverageError": abs(row[f"{prefix}_breach_rate"] - (1 - confidence)),
                    "kupiecReject": row[f"{prefix}_kupiec_p"] < .05,
                    "clusterReject": row[f"{prefix}_christoffersen_p"] < .05,
                    "esDeviation": abs(row[f"{prefix}_es_tail_loss_ratio"] - 1),
                    "runtime": row[f"runtime_{prefix}_seconds"],
                })
                qdiff[row["universe_id"]].append(ql - row["mvewma_mean_ql"])
                fdiff[row["universe_id"]].append(fz - row["mvewma_mean_fz0"])
            qregret = np.asarray([x["qlRegret"] for x in per_workload])
            fregret = np.asarray([x["fzRegret"] for x in per_workload])
            garch_share = np.mean([x["model"] == "VT-GARCH" for x in per_workload])
            comparisons.append({
                "confidence": confidence, "strategy": strategy,
                "aggregation": "equal_workload",
                "workload_frequency_status": "synthetic_equal_no_production_distribution",
                "workload_count": len(per_workload),
                "mean_ql": np.mean([x["ql"] for x in per_workload]),
                "mean_fz0": np.mean([x["fz"] for x in per_workload]),
                "coverage_error": np.mean([x["coverageError"] for x in per_workload]),
                "kupiec_rejection_rate": np.mean([x["kupiecReject"] for x in per_workload]),
                "breach_clustering_rate": np.mean([x["clusterReject"] for x in per_workload]),
                "es_ratio_deviation": np.mean([x["esDeviation"] for x in per_workload]),
                "worst_5pct_mean_ql": np.mean(sorted(x["ql"] for x in per_workload)[-max(1, len(per_workload)//20):]),
                "worst_case_ql_regret": qregret.max(),
                "runtime_seconds": np.mean([x["runtime"] for x in per_workload]),
                "fit_failure_rate": 0.0, "fallback_rate": 0.0,
                "selected_garch_share": garch_share,
                "model_switch_rate_static": 0.0,
                "ql_vs_global_mvewma": json.dumps(cluster_test(qdiff, f"ql:{confidence}:{strategy}")),
                "fz_vs_global_mvewma": json.dumps(cluster_test(fdiff, f"fz:{confidence}:{strategy}")),
            })
            for metric, values in (("ql", qregret), ("fz0", fregret)):
                regrets.append({
                    "confidence": confidence, "strategy": strategy, "metric": metric,
                    "mean_regret": values.mean(), "median_regret": np.median(values),
                    "p90_regret": np.quantile(values, .90),
                    "p95_regret": np.quantile(values, .95),
                    "maximum_regret": values.max(),
                })
            for breadth in sorted({r["nominal_asset_count"] for r in subset}):
                part = [x for x in per_workload if x["row"]["nominal_asset_count"] == breadth]
                comparisons.append({
                    "confidence": confidence, "strategy": strategy,
                    "aggregation": f"breadth_{int(breadth)}",
                    "workload_frequency_status": "stratified",
                    "workload_count": len(part),
                    "mean_ql": np.mean([x["ql"] for x in part]),
                    "mean_fz0": np.mean([x["fz"] for x in part]),
                    "coverage_error": np.mean([x["coverageError"] for x in part]),
                    "kupiec_rejection_rate": np.mean([x["kupiecReject"] for x in part]),
                    "breach_clustering_rate": np.mean([x["clusterReject"] for x in part]),
                    "es_ratio_deviation": np.mean([x["esDeviation"] for x in part]),
                    "worst_5pct_mean_ql": np.mean(sorted(x["ql"] for x in part)[-max(1, len(part)//20):]),
                    "worst_case_ql_regret": max(x["qlRegret"] for x in part),
                    "runtime_seconds": np.mean([x["runtime"] for x in part]),
                    "fit_failure_rate": 0.0, "fallback_rate": 0.0,
                    "selected_garch_share": np.mean([x["model"] == "VT-GARCH" for x in part]),
                    "model_switch_rate_static": 0.0,
                    "ql_vs_global_mvewma": "", "fz_vs_global_mvewma": "",
                })
            bins = (("hhi_lt_0.10", lambda x: x < .10), ("hhi_0.10_to_0.25", lambda x: .10 <= x < .25), ("hhi_gte_0.25", lambda x: x >= .25))
            for label, predicate in bins:
                part = [x for x in per_workload if predicate(x["row"]["hhi"])]
                if not part:
                    continue
                comparisons.append({
                    "confidence": confidence, "strategy": strategy,
                    "aggregation": label, "workload_frequency_status": "stratified",
                    "workload_count": len(part),
                    "mean_ql": np.mean([x["ql"] for x in part]),
                    "mean_fz0": np.mean([x["fz"] for x in part]),
                    "coverage_error": np.mean([x["coverageError"] for x in part]),
                    "kupiec_rejection_rate": np.mean([x["kupiecReject"] for x in part]),
                    "breach_clustering_rate": np.mean([x["clusterReject"] for x in part]),
                    "es_ratio_deviation": np.mean([x["esDeviation"] for x in part]),
                    "worst_5pct_mean_ql": np.mean(sorted(x["ql"] for x in part)[-max(1, len(part)//20):]),
                    "worst_case_ql_regret": max(x["qlRegret"] for x in part),
                    "runtime_seconds": np.mean([x["runtime"] for x in part]),
                    "fit_failure_rate": 0.0, "fallback_rate": 0.0,
                    "selected_garch_share": np.mean([x["model"] == "VT-GARCH" for x in part]),
                    "model_switch_rate_static": 0.0,
                    "ql_vs_global_mvewma": "", "fz_vs_global_mvewma": "",
                })
    return comparisons, regrets


def write_csv(path: Path, rows: list[dict]) -> None:
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader(); writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    rows = read_rows(args.results)
    (args.output / "workload_driver_regression.json").write_text(
        json.dumps(regression(rows), indent=2) + "\n"
    )
    comparisons, regrets = analyze_strategies(rows)
    write_csv(args.output / "routing_strategy_comparison.csv", comparisons)
    write_csv(args.output / "routing_regret_analysis.csv", regrets)


if __name__ == "__main__":
    main()
