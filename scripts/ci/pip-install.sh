#!/usr/bin/env bash
# CI 專用：重試整個 pip install，處理 PyPI 暫時性斷線（例如下載途中 Connection reset by peer）。
# pip 本身的 --retries 只涵蓋連線層重試，下載中途斷線仍可能直接失敗，因此外層再重跑整個安裝。
# 用法：scripts/ci/pip-install.sh -e 'src/EquityLens.Mathematics[test]'
set -euo pipefail

attempts="${PIP_INSTALL_ATTEMPTS:-3}"
delay="${PIP_INSTALL_RETRY_DELAY:-10}"

for ((i = 1; i <= attempts; i++)); do
  if python -m pip install "$@"; then
    exit 0
  fi
  if ((i == attempts)); then
    echo "::error::pip install failed after ${attempts} attempts" >&2
    exit 1
  fi
  echo "::warning::pip install attempt ${i}/${attempts} failed; retrying in ${delay}s" >&2
  sleep "$delay"
  delay=$((delay * 2))
done
