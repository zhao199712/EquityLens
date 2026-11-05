#!/usr/bin/env python3
"""Prepare/start the isolated seed runtime. Requires Docker, .NET 10, Node/npm, Python 3.

State and passwords live outside Git, default /tmp/equitylens-seed-runtime.
No downloads or AI calls occur when starting the API after setup.
"""
import argparse
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
from seed import ROOT, load_bundle, target_guard


def private_json(path, value):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'w') as f:
        json.dump(value, f, indent=2)


def environment(state):
    config = json.loads((state / 'runtime.json').read_text())
    env = dict(os.environ)
    env.update(config)
    for key in ('GH_TOKEN', 'GITHUB_TOKEN', 'E2E_ADMIN_PASSWORD'):
        env.pop(key, None)
    return env


def call(command, env=None, cwd=ROOT):
    subprocess.run([str(x) for x in command], env=env, cwd=cwd, check=True)


def setup(state, data, skip_web=False):
    load_bundle(data)
    state.mkdir(parents=True, exist_ok=True)
    config = state / 'runtime.json'
    if not config.exists():
        password = secrets.token_urlsafe(32)
        values = {'ASPNETCORE_ENVIRONMENT': 'Development', 'DOTNET_ENVIRONMENT': 'Development',
            'ConnectionStrings__PostgreSQL': f'Host=127.0.0.1;Port=55439;Database=equitylens_cloud_seed;Username=equitylens_seed;Password={password}',
            'Redis__ConnectionString': '127.0.0.1:56379,abortConnect=false',
            'Jwt__ExpireMinutes': '60', 'Jwt__RefreshTokenExpireDays': '7',
            'Jwt__Key': secrets.token_urlsafe(48), 'Jwt__Issuer': 'EquityLensSeed', 'Jwt__Audience': 'EquityLensSeed',
            'MarketData__FinMind__CachedOnly': 'true', 'BackgroundJobs__Enabled': 'false',
            'MarketData__FinMind__BaseUrl': 'http://127.0.0.1:9', 'MarketData__FinMind__Token': '',
            'MarketData__AlphaVantage__ApiKey': '', 'MarketData__AlphaVantage__BaseUrl': 'http://127.0.0.1:9',
            'RiskPython__PrimaryEnabled': 'false', 'RiskPython__ShadowEnabled': 'false',
            'ObjectStorage__ServiceUrl': 'http://127.0.0.1:9', 'ObjectStorage__PublicServiceUrl': '',
            'ObjectStorage__AccessKeyId': 'seed-no-storage', 'ObjectStorage__SecretAccessKey': 'seed-no-storage',
            'ObjectStorage__BucketName': 'seed-unavailable', 'E2E_TEST_MODE': '0',
            'Google__ClientId': 'seed-disabled.apps.googleusercontent.com',
            'AI__ChatProvider': 'DeepSeek', 'OTEL_EXPORTER_OTLP_ENDPOINT': '',
            'SEED_POSTGRES_PASSWORD': password}
        for provider in ('DeepSeek', 'Gemini', 'OpenAI', 'Jina', 'Cohere', 'Brave'):
            values[provider + '__ApiKey'] = ''
            values[provider + '__BaseUrl'] = 'http://127.0.0.1:9'
        private_json(config, values)
    env = environment(state)
    # Never adopt arbitrary existing containers (or change their credentials).
    for name in ('equitylens-seed-postgres', 'equitylens-seed-redis'):
        result = subprocess.run(['docker', 'inspect', '--format', '{{ index .Config.Labels "com.docker.compose.project" }}', name], capture_output=True, text=True)
        if result.returncode == 0 and result.stdout.strip() != 'equitylens-seed':
            raise ValueError(f'{name} already exists outside this Compose project; use a clean runtime')
    call(['docker', 'compose', '-p', 'equitylens-seed', '-f', ROOT/'scripts/seed/compose.yml', 'up', '-d', '--wait'], env)
    target_guard('equitylens-seed-postgres', 'equitylens_cloud_seed')
    call(['dotnet', 'restore', 'src/EquityLens.Api/EquityLens.Api.csproj'], env)
    tool = state/'tools/dotnet-ef'
    if not tool.exists():
        call(['dotnet', 'tool', 'install', 'dotnet-ef', '--version', '10.0.9', '--tool-path', state/'tools'], env)
    call([tool, 'database', 'update', '--project', 'src/EquityLens.Api', '--startup-project', 'src/EquityLens.Api'], env)
    call([sys.executable, ROOT/'scripts/seed/seed.py', 'import', '--container', 'equitylens-seed-postgres',
          '--database', 'equitylens_cloud_seed', '--directory', data, '--credentials', state/'credentials.json'], env)
    if not skip_web:
        call(['npm', 'ci'], env, ROOT/'src/EquityLens.Web')
    print(f'Setup complete. Credentials: {state / "credentials.json"}; API: runtime.py api --state {state}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['setup', 'api'])
    parser.add_argument('--state', type=Path, default=Path('/tmp/equitylens-seed-runtime'))
    parser.add_argument('--data', type=Path, default=Path('/tmp/equitylens-seed-v1'))
    parser.add_argument('--skip-web', action='store_true')
    args = parser.parse_args()
    if args.command == 'setup':
        setup(args.state.resolve(), args.data.resolve(), args.skip_web)
    else:
        target_guard('equitylens-seed-postgres', 'equitylens_cloud_seed')
        call(['dotnet', 'run', '--no-build', '--no-launch-profile', '--project', 'src/EquityLens.Api',
              '--urls', 'http://127.0.0.1:55440'], environment(args.state))


if __name__ == '__main__':
    main()
