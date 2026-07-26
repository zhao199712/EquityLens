"""Threshold stability and explicit failure/fallback research fixtures."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path

import numpy as np

from risk_worker.engine import (
    ewma_covariances, filtered_residuals, stable_seed, stabilize_covariances,
)
from risk_worker.research.robustness_experiments import (
    CONFIDENCES, SIMULATIONS, fit_vt, read_returns, var_es,
)


def weights_for_hhi(target: float, assets: int = 20) -> np.ndarray:
    # x^2 + (1-x)^2/(n-1) = target, larger root.
    a = 1 + 1 / (assets - 1)
    b = -2 / (assets - 1)
    c = 1 / (assets - 1) - target
    x = (-b + np.sqrt(b * b - 4 * a * c)) / (2 * a)
    result = np.full(assets, (1 - x) / (assets - 1))
    result[0] = x
    return result


def select(governance: str, hhi: float, previous: str, dwell: int, offset: int) -> str:
    if governance == "no_hysteresis":
        return "VT-GARCH" if hhi < .10 else "MVEWMA"
    if governance == "dual_threshold_hysteresis":
        if hhi < .08:
            return "VT-GARCH"
        if hhi > .12:
            return "MVEWMA"
        return previous
    if governance == "minimum_dwell":
        candidate = "VT-GARCH" if hhi < .10 else "MVEWMA"
        return candidate if dwell >= 21 else previous
    if governance == "monthly_evaluation":
        return ("VT-GARCH" if hhi < .10 else "MVEWMA") if offset % 21 == 0 else previous
    raise ValueError(governance)


def threshold_run(input_path: Path, output: Path) -> None:
    dates, returns = read_returns(input_path)
    targets = {
        "hhi_minus_0.010": .09, "hhi_minus_0.005": .095,
        "hhi_plus_0.005": .105, "hhi_plus_0.010": .11,
        "neff_minus_0.5": 1 / 9.5, "neff_plus_0.5": 1 / 10.5,
    }
    base = weights_for_hhi(.10)
    up = base.copy(); up[0] += .01; up[1:] *= (1 - up[0]) / up[1:].sum()
    down = base.copy(); down[0] -= .01; down[1:] *= (1 - down[0]) / down[1:].sum()
    initial = {name: weights_for_hhi(value) for name, value in targets.items()}
    initial["maximum_weight_plus_0.01"] = up
    initial["maximum_weight_minus_0.01"] = down
    paths = {name: weights.copy() for name, weights in initial.items()}
    governance = (
        "no_hysteresis", "dual_threshold_hysteresis",
        "minimum_dwell", "monthly_evaluation",
    )
    routing = {
        (name, rule): {"model": "MVEWMA", "dwell": 21, "events": [], "selected": []}
        for name in paths for rule in governance
    }
    parameters = residual = next_variance = None
    previous_risk = {}
    for offset, index in enumerate(range(252, returns.shape[1])):
        if offset % 21 == 0:
            params, columns, forecasts = [], [], []
            for asset in range(20):
                p, z, forecast, _ = fit_vt(returns[asset, index - 252:index] * 100)
                params.append(p); columns.append(z); forecasts.append(forecast)
            parameters, residual, next_variance = params, np.column_stack(columns), np.asarray(forecasts)
        assert parameters is not None and residual is not None and next_variance is not None
        matrix = returns[:, index - 252:index]
        covariance = stabilize_covariances(ewma_covariances(matrix, .94), .10)
        lower = np.linalg.cholesky(covariance)
        ewma_residual = filtered_residuals(matrix, lower)
        for name, weights in paths.items():
            input_hash = hashlib.sha256(returns.tobytes() + weights.tobytes()).hexdigest()
            rng = np.random.default_rng(stable_seed(input_hash, offset, False))
            sampled = ewma_residual[rng.integers(0, len(ewma_residual), SIMULATIONS)]
            md = np.exp(sampled @ lower[-1].T) @ weights - 1
            rng = np.random.default_rng(stable_seed(f"{input_hash}:garch", offset, False))
            sampled = residual[rng.integers(0, len(residual), SIMULATIONS)]
            gd = np.exp(sampled * (np.sqrt(next_variance) / 100)) @ weights - 1
            risks = {}
            for confidence in CONFIDENCES:
                risks[("MVEWMA", confidence)] = var_es(md, confidence)
                risks[("VT-GARCH", confidence)] = var_es(gd, confidence)
            hhi = float(np.sum(weights**2))
            for rule in governance:
                state = routing[(name, rule)]
                old = state["model"]
                new = select(rule, hhi, old, state["dwell"], offset)
                switched = new != old
                state["dwell"] = 1 if switched else state["dwell"] + 1
                state["model"] = new
                state["selected"].append(new)
                if switched:
                    for confidence in CONFIDENCES:
                        new_var, new_es = risks[(new, confidence)]
                        old_var, old_es = risks[(old, confidence)]
                        temporal = previous_risk.get((name, rule, confidence))
                        state["events"].append({
                            "path": name, "governance": rule,
                            "date": dates[index].isoformat(), "confidence": confidence,
                            "from_model": old, "to_model": new, "hhi": hhi,
                            "effective_assets": 1 / hhi,
                            "same_day_var_model_gap": new_var - old_var,
                            "same_day_es_model_gap": new_es - old_es,
                            "selected_var_temporal_jump": (
                                new_var - temporal[0] if temporal else float("nan")
                            ),
                            "selected_es_temporal_jump": (
                                new_es - temporal[1] if temporal else float("nan")
                            ),
                        })
                for confidence in CONFIDENCES:
                    previous_risk[(name, rule, confidence)] = risks[(new, confidence)]
            gross = np.exp(returns[:, index])
            paths[name] = weights * gross / np.sum(weights * gross)
        current = returns[:, index] * 100
        residual = np.vstack((residual[1:], current / np.sqrt(next_variance)))
        next_variance = np.asarray([
            p["omega"] + p["alpha"] * current[a] ** 2 + p["beta"] * next_variance[a]
            for a, p in enumerate(parameters)
        ])
    events = [event for state in routing.values() for event in state["events"]]
    summaries = []
    for (name, rule), state in routing.items():
        switches = len(state["events"]) // len(CONFIDENCES)
        sequence = state["selected"]
        dwell = []
        start = 0
        for index in range(1, len(sequence) + 1):
            if index == len(sequence) or sequence[index] != sequence[start]:
                dwell.append(index - start); start = index
        own_events = state["events"]
        summaries.append({
            "path": name, "governance": rule,
            "initial_hhi": float(np.sum(initial[name] ** 2)),
            "switch_count": switches,
            "switch_frequency": switches / len(sequence),
            "median_dwell_days": float(np.median(dwell)),
            "minimum_dwell_days": min(dwell),
            "maximum_absolute_same_day_var_gap": max(
                [abs(e["same_day_var_model_gap"]) for e in own_events] or [0]
            ),
            "maximum_absolute_same_day_es_gap": max(
                [abs(e["same_day_es_model_gap"]) for e in own_events] or [0]
            ),
            "maximum_absolute_selected_var_jump": max(
                [abs(e["selected_var_temporal_jump"]) for e in own_events if np.isfinite(e["selected_var_temporal_jump"])] or [0]
            ),
            "maximum_absolute_selected_es_jump": max(
                [abs(e["selected_es_temporal_jump"]) for e in own_events if np.isfinite(e["selected_es_temporal_jump"])] or [0]
            ),
        })
    write_csv(output / "threshold_stability.csv", summaries)
    if events:
        write_csv(output / "routing_switch_events.csv", events)
    else:
        (output / "routing_switch_events.csv").write_text(
            "path,governance,date,confidence,from_model,to_model,hhi,effective_assets,same_day_var_model_gap,same_day_es_model_gap,selected_var_temporal_jump,selected_es_temporal_jump\n"
        )


def fallback_fixtures(output: Path) -> None:
    fixtures = [
        ("one_asset", True, True, False, "NONE"),
        ("two_assets", True, True, False, "NONE"),
        ("new_listing_insufficient_history", False, False, False, "INSUFFICIENT_HISTORY"),
        ("heavy_missing_values", False, False, False, "MISSING_DATA"),
        ("asynchronous_dates", False, False, False, "ASYNCHRONOUS_DATES"),
        ("zero_variance", False, False, False, "ZERO_VARIANCE"),
        ("extreme_outlier", True, True, False, "NONE"),
        ("persistence_unhealthy", False, True, True, "PERSISTENCE_UNHEALTHY"),
        ("optimizer_nonconvergence", False, True, True, "OPTIMIZER_NONCONVERGENCE"),
        ("nonfinite_parameter", False, True, True, "NONFINITE_PARAMETER"),
        ("nonfinite_predictive_distribution", False, True, True, "NONFINITE_DISTRIBUTION"),
        ("worker_timeout", False, True, True, "WORKER_TIMEOUT"),
        ("database_unavailable", False, False, False, "DATABASE_UNAVAILABLE"),
        ("incomplete_adjusted_close", False, False, False, "INCOMPLETE_ADJUSTED_CLOSE"),
    ]
    fixture_json = []
    results = []
    timestamp = "2026-07-26T00:00:00+08:00"
    for name, current_healthy, mve_legal, previous, reason in fixtures:
        fixture = {
            "fixture": name, "requestedModel": "VT-GARCH",
            "currentGarchHealthy": current_healthy,
            "previousStableParametersAvailable": previous,
            "mvewmaInputLegal": mve_legal, "reasonCode": reason,
        }
        fixture_json.append(fixture)
        if current_healthy:
            selected, depth = "VT-GARCH", 0
        elif previous:
            selected, depth = "VT-GARCH_PREVIOUS_STABLE", 1
        elif mve_legal:
            selected, depth = "MVEWMA", 2
        else:
            selected, depth = "FAIL_CLOSED", 3
        run_id = hashlib.sha256(
            json.dumps(fixture, sort_keys=True).encode()
        ).hexdigest()
        results.append({
            "fixture": name, "requested_model": "VT-GARCH",
            "selected_model": selected, "reason_code": reason,
            "source_model_version": "workload-routing-v1-20260726",
            "data_quality_status": "valid" if mve_legal else "invalid",
            "parameter_health_status": "healthy" if current_healthy else "unhealthy_or_unavailable",
            "fallback_depth": depth, "timestamp": timestamp,
            "deterministic_run_identifier": run_id,
            "silent_repair": False, "confidence_changed": False,
            "window_changed": False,
        })
    (output / "failure_fallback_fixtures.json").write_text(
        json.dumps(fixture_json, indent=2) + "\n"
    )
    write_csv(output / "failure_fallback_results.csv", results)


def write_csv(path: Path, rows: list[dict]) -> None:
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader(); writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    threshold_run(args.input, args.output)
    fallback_fixtures(args.output)


if __name__ == "__main__":
    main()
