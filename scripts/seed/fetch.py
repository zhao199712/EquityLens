#!/usr/bin/env python3
"""Download a pinned PRIVATE GitHub release, verify against the Git-tracked lock."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import urllib.error
import urllib.request
from seed import load_bundle


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        redirected = super().redirect_request(request, fp, code, msg, headers, newurl)
        if redirected is not None:
            redirected.remove_header('Authorization')
        return redirected


def fetch(lock_path, output):
    lock = json.loads(Path(lock_path).read_text())
    token = os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')
    if not token:
        raise ValueError('Provide GH_TOKEN with read access to the private seed repository during setup')
    repository = lock['repository']
    if repository != 'zhao199712/EquityLens-SeedData':
        raise ValueError('Unexpected seed repository')
    opener = urllib.request.build_opener(SafeRedirect())

    def get(path, binary=False):
        request = urllib.request.Request('https://api.github.com/' + path, headers={
            'Authorization': 'Bearer ' + token, 'User-Agent': 'EquityLens-seed',
            'Accept': 'application/octet-stream' if binary else 'application/vnd.github+json'})
        with opener.open(request, timeout=120) as response:
            return response.read()

    meta = json.loads(get('repos/' + repository))
    if not meta['private']:
        raise ValueError('Seed repository must remain private')
    release = json.loads(get(f"repos/{repository}/releases/tags/{lock['tag']}"))
    assets = {a['name']: a for a in release['assets']}
    directory = Path(output)
    directory.mkdir(parents=True, exist_ok=True)
    for name, expected in lock['assets'].items():
        if name not in ('equitylens-seed-v1.json.gz', 'equitylens-seed-v2.json.gz', 'equitylens-seed-v3.json.gz', 'manifest.json'):
            raise ValueError('Unexpected asset path')
        data = get(f"repos/{repository}/releases/assets/{assets[name]['id']}", binary=True)
        if hashlib.sha256(data).hexdigest() != expected['sha256'] or len(data) != expected['bytes']:
            raise ValueError(f'Checksum mismatch: {name}')
        (directory / name).write_bytes(data)
    load_bundle(directory)
    print(f'Downloaded and validated {repository} {lock["tag"]}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--lock', default=str(Path(__file__).resolve().parents[2] / 'data/seed/release-lock.json'))
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    try:
        fetch(args.lock, args.output)
    except (ValueError, urllib.error.URLError, KeyError) as exc:
        # HTTP responses may contain signed locations. Do not print them.
        raise SystemExit(f'Seed download failed: {type(exc).__name__}; check token access, lock and release assets') from None
