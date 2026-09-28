"""Optional LLM-as-judge.

Code can check that "56.1%" appears; it cannot check that a paraphrase of a
risk section is faithful. The judge scores two things on a 1-5 rubric:

- faithfulness: is every claim supported by the cited quotes? (grounding)
- completeness: does it cover the reviewer's reference answer?

The judge only sees the cited quotes, not the full documents, so an
unsupported claim cannot hide behind "it's probably in the annual report".
Spot-check its verdicts by hand; a judge that is never audited is just a
second unverified model.
"""

from __future__ import annotations

import json
import re

import httpx

from .schema import EvalCase, JudgeResult, SystemOutput

PROMPT_VERSION = 1

SYSTEM_PROMPT = """你是嚴格的金融研究審稿人。你只根據提供的引用段落評分，不使用自己的知識。
回傳 JSON：{"faithfulness": 1-5, "completeness": 1-5, "rationale": "一到兩句理由"}

faithfulness（忠實度）：
5 = 每個事實與數字都能在引用段落中找到
3 = 大致有依據，但有一處推論超出引用內容
1 = 含有引用段落不支持或互相矛盾的數字或事實

completeness（完整度，對照參考答案）：
5 = 涵蓋參考答案所有要點
3 = 涵蓋一半左右
1 = 幾乎沒有涵蓋
若沒有參考答案，依問題本身判斷是否完整回答。"""


def _user_prompt(case: EvalCase, output: SystemOutput) -> str:
    quotes = "\n".join(
        f"[{c.index}] {c.title or ''} p.{c.page if c.page is not None else '?'}：{(c.quote or '').strip()[:1200]}"
        for c in output.citations
    ) or "（無引用）"
    reference = case.expected.reference_answer or "（無）"
    return (
        f"問題：{case.request.question}\n\n"
        f"參考答案：{reference}\n\n"
        f"系統回答：\n{output.answer}\n\n"
        f"引用段落：\n{quotes}"
    )


class OpenAICompatibleJudge:
    """Works with any /chat/completions endpoint (OpenAI, DeepSeek, local vLLM...)."""

    def __init__(self, base_url: str, api_key: str, model: str, *, transport: httpx.BaseTransport | None = None) -> None:
        self.model = model
        self._http = httpx.Client(
            base_url=base_url.rstrip("/"),
            headers={"Authorization": f"Bearer {api_key}"},
            timeout=120.0,
            transport=transport,
        )

    def judge(self, case: EvalCase, output: SystemOutput) -> JudgeResult | None:
        if not case.expected.answerable or output.error or not output.answer.strip():
            return None
        response = self._http.post(
            "/chat/completions",
            json={
                "model": self.model,
                "temperature": 0,
                "response_format": {"type": "json_object"},
                "messages": [
                    {"role": "system", "content": SYSTEM_PROMPT},
                    {"role": "user", "content": _user_prompt(case, output)},
                ],
            },
        )
        response.raise_for_status()
        content = response.json()["choices"][0]["message"]["content"]
        data = _parse_json(content)
        return JudgeResult(
            faithfulness=int(data["faithfulness"]),
            completeness=int(data["completeness"]),
            rationale=str(data.get("rationale", "")),
            model=self.model,
            prompt_version=PROMPT_VERSION,
        )


def _parse_json(content: str) -> dict:
    try:
        return json.loads(content)
    except json.JSONDecodeError:
        match = re.search(r"\{.*\}", content, re.DOTALL)
        if not match:
            raise
        return json.loads(match.group(0))
