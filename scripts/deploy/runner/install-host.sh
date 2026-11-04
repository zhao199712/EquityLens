#!/usr/bin/env bash
# Host-specific setup, run by the owner in a local terminal with sudo.
set -euo pipefail
umask 077
[[ $EUID == 0 ]] || { echo 'Run with sudo in your own terminal.' >&2; exit 1; }
[[ $(uname -m) == x86_64 ]] || { echo 'This installer is for Linux x64.' >&2; exit 1; }
setup_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo=/home/ymsh20220/EquityLens
account=equitylens-runner
runner=/opt/equitylens-actions-runner
state=/var/lib/equitylens-deploy
archive=/home/ymsh20220/.local/share/equitylens-runner-setup/actions-runner-linux-x64-2.338.2.tar.gz
checksum=baebf204307bac6193480852732f0aba2ae97b6c5597c244f4d5558390147c40
for tool in python3 docker git curl tar sha256sum runuser setfacl systemctl; do command -v "$tool" >/dev/null; done
python3 -c 'import sys; assert sys.version_info >= (3,12)'
[[ -f $archive && ! -L $archive ]] || { echo 'Download the documented runner archive first.' >&2; exit 1; }
printf '%s  %s\n' "$checksum" "$archive" | sha256sum --check --status
[[ -f "$repo/.env" && -f /home/ymsh20220/.local/state/equitylens/production-20261103/target.override.json ]]
# Existing installs are managed explicitly, never overwritten by rerunning setup.
[[ ! -e $runner && ! -e $state ]] || { echo 'Setup already exists; use register-start.sh or inspect the existing installation.' >&2; exit 1; }
! id "$account" >/dev/null 2>&1 || { echo 'Dedicated account already exists; inspect before continuing.' >&2; exit 1; }
useradd --system --create-home --home-dir /var/lib/equitylens-runner --shell /usr/sbin/nologin "$account"
usermod -aG docker "$account"
install -d -m 700 -o "$account" -g "$account" "$runner" "$state" "$state/runtime" "$state/releases"
# Only traversal is added; the runner receives private copies of runtime settings.
setfacl -m "u:$account:--x" /home/ymsh20220
install -m 600 -o "$account" -g "$account" "$repo/.env" "$state/runtime/production.env"
install -m 600 -o "$account" -g "$account" /home/ymsh20220/.local/state/equitylens/production-20261103/target.override.json "$state/runtime/runtime.override.json"
# Preserve prior releases for manual rollback; original operator backups remain intact.
if [[ -d /home/ymsh20220/.local/state/equitylens/deployments ]]; then
  cp -a /home/ymsh20220/.local/state/equitylens/deployments/. "$state/releases/"
  chown -R "$account:$account" "$state/releases"
fi
install -d -m 755 /etc/equitylens /usr/local/libexec
python3 - <<'PY'
import json,os
p='/etc/equitylens/runner-deploy.json'
with open(p,'x') as f:
 json.dump({'live_compose':'/home/ymsh20220/EquityLens/docker-compose.prod.yml',
 'env_file':'/var/lib/equitylens-deploy/runtime/production.env',
 'compose_override':'/var/lib/equitylens-deploy/runtime/runtime.override.json',
 'state_dir':'/var/lib/equitylens-deploy/releases',
 'health_url':'https://equitylens.duckdns.org/healthz'},f,indent=2)
os.chmod(p,0o644)
PY
install -m 755 "$setup_dir/guard.sh" /usr/local/libexec/equitylens-runner-guard.sh
install -m 600 -o "$account" -g "$account" "$archive" "$runner/runner.tar.gz"
runuser -u "$account" -- tar -xzf "$runner/runner.tar.gz" -C "$runner"
rm "$runner/runner.tar.gz"
# Set the hook in systemd as well when registering; protect it from accidental edits.
printf '%s\n' 'ACTIONS_RUNNER_HOOK_JOB_STARTED=/usr/local/libexec/equitylens-runner-guard.sh' > "$runner/.env"
chown "$account:$account" "$runner/.env"
runuser -u "$account" -- docker info --format '{{.ServerVersion}}' >/dev/null
runuser -u "$account" -- test -r "$repo/docker-compose.prod.yml"
printf '%s\n' 'Host files installed. No runner service started and no production container changed.'
bash "$setup_dir/register-start.sh"
