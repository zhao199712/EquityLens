"""Run RouterCapture through the shared cost-admission proxy; requires explicit budget authorization.

Build RouterCapture first. Credentials are read only from DEEPSEEK_API_KEY.
Never use this entry point as an automatically paid CI test.
"""
import argparse
import os
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from equitylens_eval.budget_proxy import Budget, serve


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cases', required=True)
    parser.add_argument('--out', required=True)
    parser.add_argument('--budget-usd', required=True, type=float)
    args = parser.parse_args()
    if not os.environ.get('DEEPSEEK_API_KEY'):
        parser.error('DEEPSEEK_API_KEY is required')
    dll = Path(__file__).parent / 'bin/Debug/net10.0/RouterCapture.dll'
    if not dll.exists():
        parser.error('Build RouterCapture.csproj first')
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    budget = Budget(args.budget_usd, out / 'budget-ledger.jsonl')
    server = serve(budget, 'https://api.deepseek.com', 5098)
    try:
        result = subprocess.run(['dotnet', str(dll), str(Path(args.cases).resolve()), str(out / 'capture')])
        print(f'Estimated upper cost: US${budget.spent:.8f}')
        return result.returncode
    finally:
        server.shutdown()


if __name__ == '__main__':
    raise SystemExit(main())
