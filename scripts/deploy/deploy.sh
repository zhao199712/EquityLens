#!/usr/bin/env bash
set -euo pipefail
# Keep orchestration/testable guards in Python's standard library; no pip install required.
exec python3 "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/deploy.py" "$@"
