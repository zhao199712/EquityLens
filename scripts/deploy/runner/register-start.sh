#!/usr/bin/env bash
set -euo pipefail
umask 077
[[ $EUID == 0 ]] || { echo 'Run with sudo in your own terminal.' >&2; exit 1; }
runner=/opt/equitylens-actions-runner
account=equitylens-runner
[[ -f /etc/equitylens/runner-deploy.json && -f /usr/local/libexec/equitylens-runner-guard.sh ]]
cd "$runner"
if [[ ! -f .runner ]]; then
  printf '%s\n' 'Open https://github.com/zhao199712/EquityLens/settings/actions/runners/new?arch=x64&os=linux' 'Copy only the short-lived registration token. Input is hidden; do not paste it into chat.'
  read -r -s -p 'Registration token: ' ACTIONS_RUNNER_INPUT_TOKEN </dev/tty
  printf '\n'
  [[ -n $ACTIONS_RUNNER_INPUT_TOKEN ]]
  export ACTIONS_RUNNER_INPUT_TOKEN
  trap 'unset ACTIONS_RUNNER_INPUT_TOKEN' EXIT
  # Pass the token via environment, not command arguments or shell history.
  runuser -u "$account" -- ./config.sh --unattended \
    --url https://github.com/zhao199712/EquityLens \
    --name equitylens-prod-linux --labels equitylens-prod --no-default-labels --work _work
  unset ACTIONS_RUNNER_INPUT_TOKEN
fi
if [[ ! -f .service ]]; then ./svc.sh install "$account"; fi
service=$(cat .service)
[[ $service == actions.runner.*.service && $service != */* ]]
install -d -m 755 "/etc/systemd/system/$service.d"
printf '%s\n' '[Service]' 'Environment=ACTIONS_RUNNER_HOOK_JOB_STARTED=/usr/local/libexec/equitylens-runner-guard.sh' 'UMask=0077' > "/etc/systemd/system/$service.d/equitylens.conf"
systemctl daemon-reload
./svc.sh start
./svc.sh status
printf '%s\n' 'Runner service started. First dispatch Deploy backend with mode=verify after main CI is green.'
