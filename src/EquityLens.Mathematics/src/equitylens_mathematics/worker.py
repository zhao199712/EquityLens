from __future__ import annotations

import gzip
import hashlib
import json
import logging
import os
import signal
import time
from typing import Any

import redis

from .engine import calculate_backtest, calculate_current_risk
from .garch import GarchFitError
from .models import RiskBacktestInput


logging.basicConfig(
    level=os.getenv("LOG_LEVEL", "INFO"),
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
)
LOGGER = logging.getLogger("equitylens.equitylens_mathematics")
RUNNING = True


def environment(name: str, default: str) -> str:
    return os.getenv(name, default)


REDIS_URL = environment("REDIS_URL", "redis://localhost:6379/0")
JOBS_STREAM = environment("RISK_JOBS_STREAM", "equitylens:risk-python:jobs")
JOBS_GROUP = environment("RISK_JOBS_GROUP", "risk-python-workers")
RESULTS_STREAM = environment("RISK_RESULTS_STREAM", "equitylens:risk-python:results")
DEAD_LETTER_STREAM = environment(
    "RISK_DEAD_LETTER_STREAM", "equitylens:risk-python:dead-letter"
)
PAYLOAD_PREFIX = environment("RISK_PAYLOAD_PREFIX", "equitylens:risk-python")
CONSUMER = environment(
    "RISK_WORKER_CONSUMER", f"python-risk-{os.uname().nodename}-{os.getpid()}"
)
TTL_SECONDS = int(environment("RISK_PAYLOAD_TTL_SECONDS", "86400"))
MAX_ATTEMPTS = int(environment("RISK_MAX_ATTEMPTS", "3"))
PENDING_MIN_IDLE_MS = int(environment("RISK_PENDING_MIN_IDLE_MS", "300000"))


def stop(_signum: int, _frame: Any) -> None:
    global RUNNING
    RUNNING = False


def ensure_group(client: redis.Redis) -> None:
    try:
        client.xgroup_create(JOBS_STREAM, JOBS_GROUP, id="0", mkstream=True)
    except redis.ResponseError as exception:
        if "BUSYGROUP" not in str(exception):
            raise


def text(fields: dict[bytes, bytes], name: str, default: str = "") -> str:
    value = fields.get(name.encode())
    return value.decode() if value is not None else default


def publish_result(
    client: redis.Redis,
    fields: dict[bytes, bytes],
    status: str,
    result: dict | None,
    error: str = "",
) -> None:
    job_id = text(fields, "jobId")
    comparison_id = text(fields, "comparisonId")
    calculation_run_id = text(fields, "calculationRunId")
    duration = int(result.get("_durationMs", 0)) if result else 0
    result_key = ""
    if result is not None:
        result_key = f"{PAYLOAD_PREFIX}:result:{job_id.replace('-', '')}"
        persisted_result = {key: value for key, value in result.items() if key != "_durationMs"}
        encoded = gzip.compress(
            json.dumps(persisted_result, separators=(",", ":"), ensure_ascii=False).encode()
        )
        client.setex(result_key, TTL_SECONDS, encoded)
    client.xadd(
        RESULTS_STREAM,
        {
            "jobId": job_id,
            "comparisonId": comparison_id,
            "calculationRunId": calculation_run_id,
            "status": status,
            "resultKey": result_key,
            "durationMs": str(duration),
            "error": error,
            "completedAtUtc": str(time.time()),
        },
    )


def process(client: redis.Redis, message_id: bytes, fields: dict[bytes, bytes]) -> None:
    started = time.perf_counter()
    try:
        input_key = text(fields, "inputKey")
        expected_hash = text(fields, "inputHash")
        compressed = client.get(input_key)
        if compressed is None:
            raise ValueError(f"input payload {input_key!r} is missing or expired")
        raw = gzip.decompress(compressed).decode("utf-8-sig")
        actual_hash = hashlib.sha256(raw.encode()).hexdigest()
        if actual_hash != expected_hash:
            raise ValueError("canonical input hash mismatch")
        input_data = RiskBacktestInput.model_validate_json(raw)
        operation = text(fields, "operation", "vt-garch-backtest")
        result = (
            calculate_backtest(input_data, actual_hash)
            if operation in {"vt-garch-backtest", "backtest"}
            else calculate_current_risk(input_data, actual_hash, operation)
        )
        result["_durationMs"] = round((time.perf_counter() - started) * 1000)
        publish_result(client, fields, "Completed", result)
        client.xack(JOBS_STREAM, JOBS_GROUP, message_id)
        LOGGER.info("completed job_id=%s duration_ms=%s", text(fields, "jobId"), result["_durationMs"])
    except Exception as exception:
        attempt = int(text(fields, "attempt", "1"))
        LOGGER.exception("failed job_id=%s attempt=%s", text(fields, "jobId"), attempt)
        client.xack(JOBS_STREAM, JOBS_GROUP, message_id)
        deterministic_failure = isinstance(exception, (GarchFitError, ValueError))
        if attempt < MAX_ATTEMPTS and not deterministic_failure:
            retried = {
                key.decode(): value.decode()
                for key, value in fields.items()
            }
            retried["attempt"] = str(attempt + 1)
            retried["lastError"] = str(exception)
            client.xadd(JOBS_STREAM, retried)
        else:
            dead = {key.decode(): value.decode() for key, value in fields.items()}
            dead["error"] = str(exception)
            dead["reasonCode"] = (
                "model_health_failed" if deterministic_failure
                else "transport_retry_exhausted"
            )
            client.xadd(DEAD_LETTER_STREAM, dead)
            publish_result(client, fields, "Failed", None, str(exception))


def main() -> None:
    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    client = redis.Redis.from_url(REDIS_URL, decode_responses=False)
    ensure_group(client)
    LOGGER.info("worker started consumer=%s stream=%s", CONSUMER, JOBS_STREAM)
    last_reclaim = 0.0
    while RUNNING:
        messages = client.xreadgroup(
            JOBS_GROUP,
            CONSUMER,
            {JOBS_STREAM: ">"},
            count=1,
            block=1000,
        )
        if not messages:
            if time.monotonic() - last_reclaim >= 30:
                claimed = client.xautoclaim(
                    JOBS_STREAM,
                    JOBS_GROUP,
                    CONSUMER,
                    PENDING_MIN_IDLE_MS,
                    "0-0",
                    count=1,
                )
                last_reclaim = time.monotonic()
                entries = claimed[1] if len(claimed) > 1 else []
                for message_id, fields in entries:
                    process(client, message_id, fields)
            continue
        for _stream, entries in messages:
            for message_id, fields in entries:
                process(client, message_id, fields)
    LOGGER.info("worker stopped")


if __name__ == "__main__":
    main()
