import csv
import json
from pathlib import Path

import numpy as np

from equitylens_mathematics.research.routing_safety_validation import (
    select,
    weights_for_hhi,
)
from equitylens_mathematics.research.workload_routing_validation import (
    _merge_csv,
    _shard_for,
)


def test_manifest_workload_sharding_is_deterministic_and_complete():
    ids = [f"workload-{index}" for index in range(500)]
    first = [_shard_for(value, 8) for value in ids]
    second = [_shard_for(value, 8) for value in ids]
    assert first == second
    assert set(first) == set(range(8))
    assert all(0 <= shard < 8 for shard in first)


def test_checkpoint_csv_merge_is_deterministically_sorted(tmp_path: Path):
    parts = []
    for index, rows in enumerate(
        (({"id": "b", "value": "2"},), ({"id": "a", "value": "1"},))
    ):
        path = tmp_path / f"part-{index}.csv"
        with path.open("w", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=("id", "value"))
            writer.writeheader()
            writer.writerows(rows)
        parts.append(path)
    target = tmp_path / "merged.csv"
    _merge_csv(parts, target, ("id",))
    assert [row["id"] for row in csv.DictReader(target.open())] == ["a", "b"]


def test_hhi_fixture_hits_requested_boundary():
    for target in (0.08, 0.09, 0.095, 0.10, 0.105, 0.11, 0.12):
        weights = weights_for_hhi(target)
        assert np.all(weights >= 0)
        assert np.isclose(weights.sum(), 1)
        assert np.isclose(np.sum(weights**2), target)


def test_routing_governance_is_predeclared_and_deterministic():
    assert select("no_hysteresis", 0.099, "MVEWMA", 30, 5) == "VT-GARCH"
    assert select("dual_threshold_hysteresis", 0.10, "MVEWMA", 30, 5) == "MVEWMA"
    assert select("minimum_dwell", 0.099, "MVEWMA", 20, 5) == "MVEWMA"
    assert select("monthly_evaluation", 0.099, "MVEWMA", 30, 20) == "MVEWMA"


def test_routing_manifest_hash_is_frozen():
    root = Path(__file__).resolve().parents[3]
    manifest = root / "docs/workload-routing-validation/routing_rule_manifest.json"
    payload = json.loads(manifest.read_text())
    assert payload["immutableAfterEvaluationStarts"] is True
    assert payload["strategies"]["C_hhi"]["if"]["hhiLt"] == 0.10

