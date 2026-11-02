#!/usr/bin/env python3
"""Run an authenticated AgentLoop demo; requires only Python's standard library.

Set EQUITYLENS_BASE_URL and EQUITYLENS_TOKEN. Token is never printed.
This submits a real research request and may incur provider charges.
"""
import argparse
import json
import os
import time
import urllib.error
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ticker', default='2330')
    parser.add_argument('--question', default='台積電 2025 年基本每股盈餘是多少？')
    parser.add_argument('--timeout', type=int, default=600)
    args = parser.parse_args()
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
