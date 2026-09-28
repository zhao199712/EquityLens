"""HTTP client for the EquityLens API.

Two modes produce the same `SystemOutput`, so one dataset can compare them:

- ask:           POST /api/research/ask (single-shot retrieve -> answer)
- investigation: POST /api/research/investigations, poll the agent run until
                 it reaches a terminal state, then read the research run
                 artifact (planner -> retrieval -> critic -> revision)
"""

from __future__ import annotations

import time
from datetime import datetime
from typing import Protocol

import httpx

from .schema import Citation, ResearchRequest, SystemOutput

TERMINAL_AGENT_STATUSES = {"Succeeded", "Failed", "Cancelled"}


class ResearchSystem(Protocol):
    def run(self, request: ResearchRequest) -> SystemOutput: ...


class EquityLensClient:
    def __init__(
        self,
        base_url: str,
        *,
        token: str | None = None,
        email: str | None = None,
        password: str | None = None,
        timeout_seconds: float = 180.0,
        transport: httpx.BaseTransport | None = None,
    ) -> None:
        self._http = httpx.Client(base_url=base_url.rstrip("/"), timeout=timeout_seconds, transport=transport)
        if token:
            self._set_token(token)
        elif email and password:
            self.login(email, password)

    def _set_token(self, token: str) -> None:
        self._http.headers["Authorization"] = f"Bearer {token}"

    def login(self, email: str, password: str) -> None:
        response = self._http.post("/api/auth/login", json={"email": email, "password": password})
        response.raise_for_status()
        self._set_token(response.json()["accessToken"])

    def post(self, path: str, body: dict) -> httpx.Response:
        return self._http.post(path, json=body)

    def get(self, path: str) -> httpx.Response:
        return self._http.get(path)

    def close(self) -> None:
        self._http.close()


def _error_text(response: httpx.Response) -> str:
    try:
        body = response.json()
        return f"HTTP {response.status_code}: {body.get('code') or ''} {body.get('message') or body}".strip()
    except ValueError:
        return f"HTTP {response.status_code}: {response.text[:300]}"


class AskMode:
    """Single-shot RAG through /api/research/ask."""

    name = "ask"

    def __init__(self, client: EquityLensClient) -> None:
        self._client = client

    def run(self, request: ResearchRequest) -> SystemOutput:
        started = time.perf_counter()
        try:
            response = self._client.post("/api/research/ask", request.to_api())
        except httpx.HTTPError as exc:
            return SystemOutput(status="Error", answer="", error=f"transport: {exc}")
        wall_ms = int((time.perf_counter() - started) * 1000)
        if response.status_code >= 400:
            return SystemOutput(status="Error", answer="", latency_ms=wall_ms, error=_error_text(response))
        body = response.json()
        trace = body.get("trace") or {}
        usage = trace.get("tokenUsage") or {}
        return SystemOutput(
            status=body.get("status") or "Answered",
            answer=body.get("answer") or "",
            citations=[
                Citation(
                    index=c["index"],
                    document_type=c.get("documentType"),
                    title=c.get("title"),
                    page=c.get("pageNumber"),
                    quote=c.get("quoteText"),
                )
                for c in body.get("citations") or []
            ],
            latency_ms=wall_ms,
            input_tokens=usage.get("promptTokens"),
            output_tokens=usage.get("completionTokens"),
            research_run_id=body.get("researchRunId"),
        )


class InvestigationMode:
    """Full agent workflow through /api/research/investigations."""

    name = "investigation"

    def __init__(
        self,
        client: EquityLensClient,
        *,
        poll_interval_seconds: float = 2.0,
        max_wait_seconds: float = 600.0,
        sleep=time.sleep,
    ) -> None:
        self._client = client
        self._poll = poll_interval_seconds
        self._max_wait = max_wait_seconds
        self._sleep = sleep

    def run(self, request: ResearchRequest) -> SystemOutput:
        started = time.perf_counter()
        try:
            created = self._client.post("/api/research/investigations", request.to_api())
            if created.status_code >= 400:
                return SystemOutput(status="Error", answer="", error=_error_text(created))
            ids = created.json()
            agent_run_id, research_run_id = ids["agentRunId"], ids["researchRunId"]

            run: dict = {}
            while True:
                detail = self._client.get(f"/api/agent-runs/{agent_run_id}")
                if detail.status_code >= 400:
                    return SystemOutput(status="Error", answer="", agent_run_id=agent_run_id, error=_error_text(detail))
                run = detail.json()["run"]
                if run["status"] in TERMINAL_AGENT_STATUSES:
                    break
                if time.perf_counter() - started > self._max_wait:
                    return SystemOutput(
                        status="Error", answer="", agent_run_id=agent_run_id, research_run_id=research_run_id,
                        error=f"timed out after {self._max_wait:.0f}s in status {run['status']}",
                    )
                self._sleep(self._poll)

            artifact = self._client.get(f"/api/research/runs/{research_run_id}")
            if artifact.status_code >= 400:
                return SystemOutput(status="Error", answer="", agent_run_id=agent_run_id, error=_error_text(artifact))
            body = artifact.json()
        except httpx.HTTPError as exc:
            return SystemOutput(status="Error", answer="", error=f"transport: {exc}")

        latency = _run_latency_ms(run) or int((time.perf_counter() - started) * 1000)
        error = None
        if run["status"] != "Succeeded":
            error = f"agent run {run['status']}: {run.get('errorMessage') or 'no error message'}"
        return SystemOutput(
            status=body["run"]["status"],
            answer=body.get("answer") or "",
            citations=[
                Citation(
                    index=c["citationIndex"],
                    document_type=c.get("documentType"),
                    title=c.get("title"),
                    page=c.get("pageNumber"),
                    quote=c.get("quoteText"),
                )
                for c in body.get("citations") or []
            ],
            latency_ms=latency,
            input_tokens=run.get("totalInputTokens"),
            output_tokens=run.get("totalOutputTokens"),
            cost_usd=float(run["totalEstimatedCostUsd"]) if run.get("totalEstimatedCostUsd") is not None else None,
            research_run_id=research_run_id,
            agent_run_id=agent_run_id,
            error=error,
        )


def _run_latency_ms(run: dict) -> int | None:
    start, end = run.get("startedAtUtc") or run.get("createdAtUtc"), run.get("completedAtUtc")
    if not start or not end:
        return None
    parse = lambda s: datetime.fromisoformat(s.replace("Z", "+00:00"))  # noqa: E731
    return int((parse(end) - parse(start)).total_seconds() * 1000)
