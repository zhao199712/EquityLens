#!/usr/bin/env python3
"""Run an authenticated AgentLoop demo; requires only Python's standard library.

Set EQUITYLENS_BASE_URL and EQUITYLENS_TOKEN. Token is never printed.
Live mode submits a real research request and may incur provider charges.
Use --replay responses.jsonl --case CASE_ID for a saved, offline demonstration.
"""
import argparse
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.request


def replay(path, case_id, show_quotes=False):
    """Display saved evidence without contacting the API or re-scoring it."""
    records = [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines() if line.strip()]
    matches = [r for r in records if r.get('case_id') == case_id]
    if len(matches) != 1:
        raise ValueError(f'Expected one saved case {case_id!r}, found {len(matches)}. Available: ' +
                         ', '.join(r.get('case_id', '(missing id)') for r in records))
    saved = matches[0]
    print('OFFLINE REPLAY — saved response; no API request submitted')
    print('Source:', path)
    meta_path = path.parent / 'run_meta.json'
    if meta_path.exists():
        meta = json.loads(meta_path.read_text(encoding='utf-8'))
        print('Engine:', meta.get('engine', 'not recorded'), 'backend:', meta.get('system_commit', 'not recorded'))
        print('Recorded at:', meta.get('started_at', 'not recorded'))
    print('Case:', case_id, 'run:', saved.get('run_id'))
    print('Research status:', saved.get('status'), 'recorded latency (ms):', saved.get('latency_ms'))
    results_path = path.parent / 'results.jsonl'
    if results_path.exists():
        results = [json.loads(line) for line in results_path.read_text(encoding='utf-8').splitlines() if line.strip()]
        result = next((r for r in results if r.get('case_id') == case_id), None)
        if result is not None:
            print('Saved evaluation:', {True: 'PASS', False: 'FAIL', None: 'NOT COUNTED'}[result.get('passed')])
            print('Scores:', json.dumps(result.get('scores', {}), ensure_ascii=False))
    trajectory = saved.get('trajectory') or {}
    if trajectory:
        print('Recorded loop turns:', trajectory.get('llm_turns'), 'stop:', trajectory.get('stop_reason'))
        print('Tool counts:', json.dumps(trajectory.get('tool_calls_by_name', {}), ensure_ascii=False))
        print('Guard denials:', trajectory.get('guard_denials'), 'Web calls:', trajectory.get('web_calls'))
    if saved.get('error'):
        print('Recorded error:', saved['error'])
    print('\n' + saved.get('answer', ''))
    print('\nReferences:')
    for citation in saved.get('citations') or []:
        print(f"  [{citation['index']}] {citation.get('title')} page {citation.get('page')}")
        if show_quotes:
            print(citation.get('quote') or '(no saved quote)')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ticker', default='2330')
    parser.add_argument('--question', default='台積電 2025 年基本每股盈餘是多少？')
    parser.add_argument('--timeout', type=int, default=600)
    parser.add_argument('--replay', type=Path, help='saved responses.jsonl; does not use network or JWT')
    parser.add_argument('--case', default='2330-eps-2025', help='case ID for offline replay')
    parser.add_argument('--show-quotes', action='store_true', help='include saved source quotations in replay')
    args = parser.parse_args()
    if args.replay:
        try:
            replay(args.replay, args.case, args.show_quotes)
        except (OSError, ValueError, KeyError) as exc:
            parser.error(str(exc))
        return
    base = os.getenv('EQUITYLENS_BASE_URL', 'http://localhost:5035').rstrip('/')
    token = os.getenv('EQUITYLENS_TOKEN')
    if not token:
        parser.error('Set EQUITYLENS_TOKEN to a JWT for the selected API.')

    def request(path, body=None):
        req = urllib.request.Request(base + path, data=json.dumps(body).encode() if body is not None else None,
                                     headers={'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'})
        with urllib.request.urlopen(req, timeout=45) as response:
            return json.load(response)

    ids = request('/api/research/investigations', dict(ticker=args.ticker, question=args.question,
                  sourcePolicy='LocalOnly', retrievalMode='AnnualReportOnly', engine='AgentLoop'))
    print('Agent run:', ids['agentRunId'], flush=True)
    deadline = time.monotonic() + args.timeout
    previous = None
    while time.monotonic() < deadline:
        detail = request('/api/agent-runs/' + ids['agentRunId'])
        status = detail['run']['status']
        progress = (status, sum(n['status'] == 'Succeeded' for n in detail['nodes']))
        if progress != previous:
            print('Status:', status, 'completed nodes:', progress[1], flush=True)
            previous = progress
        if status in {'Succeeded', 'Failed', 'Cancelled', 'WaitingForFeedback', 'WaitingForApproval'}:
            break
        time.sleep(2)
    else:
        raise TimeoutError('Demo timed out. The server run may still be active; inspect its detail before retrying.')
    for turn in detail.get('llmTurns') or []:
        print(f"  {turn['mode']} turn {turn['turn']}: {turn['action']}")
    for call in detail.get('toolCalls') or []:
        print(f"  tool {call['toolName']}: {call['status']} ({call.get('guardReason') or call.get('guardDecision') or 'legacy'})")
    research = request('/api/research/runs/' + ids['researchRunId'])
    print('\nResearch status:', research['run']['status'])
    print(research.get('answer', ''))
    print('\nReferences:')
    for citation in research.get('citations') or []:
        print(f"  [{citation['citationIndex']}] {citation.get('title')} page {citation.get('pageNumber')}")
    if status != 'Succeeded':
        raise SystemExit(1)


if __name__ == '__main__':
    main()
