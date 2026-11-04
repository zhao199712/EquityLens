#!/usr/bin/env bash
# Installed outside the writable runner directory; fail before any job steps.
set -euo pipefail
if [[ "${GITHUB_REPOSITORY:-}" != 'zhao199712/EquityLens' ||
      "${GITHUB_EVENT_NAME:-}" != 'workflow_dispatch' ||
      "${GITHUB_REF:-}" != 'refs/heads/main' ||
      "${GITHUB_WORKFLOW_REF:-}" != 'zhao199712/EquityLens/.github/workflows/deploy.yml@refs/heads/main' ]]; then
  echo 'This runner only accepts the manual main deployment workflow.' >&2
  exit 1
fi
