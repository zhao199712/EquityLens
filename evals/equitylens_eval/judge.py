"""LLM-as-judge for what deterministic scorers cannot see: correctness of prose
answers and whether each claim is actually supported by the quoted citations.

Talks to any OpenAI-compatible /chat/completions endpoint (OpenAI, DeepSeek,
Gemini's OpenAI-compatible endpoint, a local vLLM...). Configure with:

  EVAL_JUDGE_BASE_URL   e.g. https://api.deepseek.com/v1
  EVAL_JUDGE_API_KEY
  EVAL_JUDGE_MODEL      e.g. deepseek-chat

Rules of thumb baked in:
  * the judge should be a different model family from the one under test
    (self-grading inflates scores),
  * temperature 0 + a versioned rubric so scores are comparable across runs,
  * spot-check ~10% of judgements by hand before trusting the numbers.
"""

from __future__ import annotations

import json
import os
import re
from dataclasses import dataclass
from typing import Any

import httpx

from .schema import EvalCase, RunOutput
from .scorers import ScoreItem, detected_behavior

JUDGE_PROMPT_VERSION = 1

_SYSTEM = """你是嚴格的金融研究答案評審。你只根據「參考答案」與「引用原文」評分，不使用你自己的知識補充事實。
請輸出 JSON，不要輸出其他文字：
{"correctness": 1-5, "groundedness": 1-5, "completeness": 1-5, "unsupported_claims": ["..."], "rationale": "一句話"}

評分標準：
- correctness：答案的事實與結論是否與參考答案一致。數字錯誤直接 ≤2。
- groundedness：答案中的每個具體主張，是否都能在「引用原文」中找到支持。有任何無出處的數字或結論就 ≤3。
- completeness：參考答案的要點是否都有涵蓋。
- unsupported_claims：列出在引用原文中找不到支持的主張（最多 5 個）。"""


@dataclass(frozen=True)
class JudgeConfig:
    base_url: str
    api_key: str
    model: str
    timeout_s: float = 60.0

    @staticmethod
    def from_env() -> "JudgeConfig | None":
        base_url, api_key, model = (os.getenv("EVAL_JUDGE_BASE_URL"), os.getenv("EVAL_JUDGE_API_KEY"),
                                    os.getenv("EVAL_JUDGE_MODEL"))
        if not (base_url and api_key and model):
            return None
        return JudgeConfig(base_url.rstrip("/"), api_key, model)


def build_user_prompt(case: EvalCase, output: RunOutput) -> str:
    quotes = "\n".join(
        f"[{c.index}] {c.title or ''} p.{c.page if c.page is not None else '?'}：{(c.quote or '').strip()[:800]}"
        for c in output.citations
    ) or "（無引用）"
    return (
        f"問題：{case.question}\n\n"
        f"參考答案：{case.reference_answer}\n\n"
        f"待評答案：\n{output.answer}\n\n"
        f"引用原文：\n{quotes}"
    )


def parse_judgement(text: str) -> dict[str, Any]:
    match = re.search(r"\{.*\}", text, re.DOTALL)
    if not match:
        raise ValueError("judge returned no JSON object")
    data = json.loads(match.group(0))
    for key in ("correctness", "groundedness", "completeness"):
        value = int(data[key])
        if not 1 <= value <= 5:
            raise ValueError(f"judge {key} out of range: {value}")
        data[key] = value
    return data


def _to_unit(score: int) -> float:
    return (score - 1) / 4


class LlmJudge:
    def __init__(self, config: JudgeConfig, transport: httpx.BaseTransport | None = None) -> None:
        self.config = config
        self._http = httpx.Client(
            base_url=config.base_url,
            headers={"Authorization": f"Bearer {config.api_key}"},
            timeout=config.timeout_s,
            transport=transport,
        )

    def close(self) -> None:
        self._http.close()

    def judge(self, case: EvalCase, output: RunOutput) -> list[ScoreItem]:
        # Only prose answers with a reference are worth judging; refusals are scored by `behavior`.
        if not case.reference_answer or detected_behavior(output) != "answer":
            return [ScoreItem("judge_correctness", None), ScoreItem("judge_groundedness", None)]
        try:
            response = self._http.post("/chat/completions", json={
                "model": self.config.model,
                "temperature": 0,
                "messages": [{"role": "system", "content": _SYSTEM},
                             {"role": "user", "content": build_user_prompt(case, output)}],
            })
            response.raise_for_status()
            verdict = parse_judgement(response.json()["choices"][0]["message"]["content"])
        except (httpx.HTTPError, ValueError, KeyError, json.JSONDecodeError) as exc:
            error = {"error": f"{type(exc).__name__}: {exc}"}
            return [ScoreItem("judge_correctness", None, error), ScoreItem("judge_groundedness", None, error)]
        meta = {"model": self.config.model, "prompt_version": JUDGE_PROMPT_VERSION,
                "rationale": verdict.get("rationale"), "unsupported_claims": verdict.get("unsupported_claims", []),
                "completeness": verdict["completeness"]}
        return [
            ScoreItem("judge_correctness", _to_unit(verdict["correctness"]), meta),
            ScoreItem("judge_groundedness", _to_unit(verdict["groundedness"]), meta),
        ]
