"""HTTP client that runs one eval case against the EquityLens API.

Black-box by design: the harness only talks to the public API, so it evaluates
exactly what a user would get and needs no access to internals.

Modes:
  ask            POST /api/research/ask (single-pass RAG, synchronous)
  investigation  POST /api/research/investigations, poll /api/agent-runs/{id},
                 then read /api/research/runs/{researchRunId} (full agent workflow)
"""

from __future__ import annotations

import time
from typing import Any

import httpx

from .schema import Citation, EvalCase, RetrievedCandidate, RunOutput

TERMINAL_RUN_STATUSES = {"Succeeded", "Failed", "Cancelled"}


class EquityLensClient:
    def __init__(self, base_url: str, token: str | None = None, timeout_s: float = 180.0,
                 poll_interval_s: float = 2.0, investigation_timeout_s: float = 900.0,
                 transport: httpx.BaseTransport | None = None) -> None:
        headers = {"Accept": "application/json"}
        if token:
            headers["Authorization"] = f"Bearer {token}"
        self._http = httpx.Client(base_url=base_url.rstrip("/"), headers=headers,
                                  timeout=timeout_s, transport=transport)
        self._poll_interval_s = poll_interval_s
        self._investigation_timeout_s = investigation_timeout_s

    def close(self) -> None:
        self._http.close()

    def __enter__(self) -> "EquityLensClient":
        return self

    def __exit__(self, *_: object) -> None:
        self.close()

    @staticmethod
    def build_request(case: EvalCase, debug: bool) -> dict[str, Any]:
        body: dict[str, Any] = {"ticker": case.ticker, "question": case.question, "debug": debug}
        body.update(case.request)  # sourcePolicy, retrievalMode, documentType, topK, portfolioId...
        return body

    def run(self, case: EvalCase, mode: str) -> RunOutput:
        started = time.perf_counter()
        try:
            output = self._ask(case) if mode == "ask" else self._investigate(case)
        except (httpx.HTTPError, TimeoutError, ValueError, KeyError) as exc:
            output = RunOutput(case_id=case.id, mode=mode, status="Error", error=_describe(exc))
        if output.latency_ms is None:
            output.latency_ms = round((time.perf_counter() - started) * 1000)
        return output

    # ---- ask --------------------------------------------------------------------------
    def _ask(self, case: EvalCase) -> RunOutput:
        response = self._http.post("/api/research/ask", json=self.build_request(case, debug=True))
        response.raise_for_status()
        return parse_ask_response(case.id, response.json())

    # ---- investigation ----------------------------------------------------------------
    def _investigate(self, case: EvalCase) -> RunOutput:
        created = self._http.post("/api/research/investigations", json=self.build_request(case, debug=False))
        created.raise_for_status()
        ids = created.json()
        agent_run_id, research_run_id = ids["agentRunId"], ids["researchRunId"]

        deadline = time.monotonic() + self._investigation_timeout_s
        summary: dict[str, Any] = {}
        while time.monotonic() < deadline:
            detail = self._http.get(f"/api/agent-runs/{agent_run_id}")
            detail.raise_for_status()
            summary = detail.json()["run"]
            if summary["status"] in TERMINAL_RUN_STATUSES:
                break
            time.sleep(self._poll_interval_s)
        else:
            raise TimeoutError(f"agent run {agent_run_id} did not finish in {self._investigation_timeout_s:.0f}s")

        research = self._http.get(f"/api/research/runs/{research_run_id}")
        research.raise_for_status()
        return parse_investigation(case.id, summary, research.json())


def parse_ask_response(case_id: str, body: dict[str, Any]) -> RunOutput:
    trace = body.get("trace") or {}
    usage = trace.get("tokenUsage") or {}
    latency = (trace.get("latencyMs") or {}).get("total")
    return RunOutput(
        case_id=case_id,
        mode="ask",
        status=body.get("status", "Answered"),
        answer=body.get("answer", ""),
        citations=[
            Citation(index=c["index"], source_type=c.get("sourceType"), document_type=c.get("documentType"),
                     page=c.get("pageNumber"), title=c.get("title"), quote=c.get("quoteText"), url=c.get("url"))
            for c in body.get("citations") or []
        ],
        candidates=[
            RetrievedCandidate(document_type=r.get("documentType"), page=r.get("pageNumber"),
                               title=r.get("documentTitle"),
                               rank=r.get("rankAfterRerank") or r.get("rankBeforeRerank"),
                               selected=bool(r.get("selected")))
            for r in trace.get("results") or []
        ],
        prompt_tokens=usage.get("promptTokens"),
        completion_tokens=usage.get("completionTokens"),
        latency_ms=latency,
        model=body.get("model"),
        run_id=body.get("researchRunId"),
    )


def parse_investigation(case_id: str, summary: dict[str, Any], research: dict[str, Any]) -> RunOutput:
    run = research.get("run") or {}
    agent_status = summary.get("status")
    status = run.get("status") or ("Failed" if agent_status != "Succeeded" else "Answered")
    if agent_status in ("Failed", "Cancelled"):
        status = "Failed"
    return RunOutput(
        case_id=case_id,
        mode="investigation",
        status=status,
        answer=research.get("answer", ""),
        citations=[
            Citation(index=c["citationIndex"], source_type=c.get("sourceType"), document_type=c.get("documentType"),
                     page=c.get("pageNumber"), title=c.get("title"), quote=c.get("quoteText"))
            for c in research.get("citations") or []
        ],
        candidates=[
            RetrievedCandidate(document_type=c.get("documentType"), page=c.get("pageNumber"), title=c.get("title"),
                               rank=c.get("rankAfterRerank") or c.get("rankBeforeRerank"),
                               selected=c.get("decision") == "Selected")
            for c in research.get("candidates") or []
        ],
        prompt_tokens=summary.get("totalInputTokens"),
        completion_tokens=summary.get("totalOutputTokens"),
        cost_usd=float(summary["totalEstimatedCostUsd"]) if summary.get("totalEstimatedCostUsd") is not None else None,
        latency_ms=run.get("latencyMs"),
        run_id=summary.get("id"),
        error=summary.get("errorMessage"),
    )


def _describe(exc: Exception) -> str:
    if isinstance(exc, httpx.HTTPStatusError):
        body = exc.response.text[:300]
        return f"HTTP {exc.response.status_code}: {body}"
    return f"{type(exc).__name__}: {exc}"
