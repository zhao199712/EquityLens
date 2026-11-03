#!/usr/bin/env python3
"""Manual, fail-closed deployment of EquityLens api/mathematics only (Linux)."""
import argparse
import copy
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import tarfile
import time
import urllib.request
import urllib.parse
import uuid

REPOSITORY = "zhao199712/EquityLens"
PROJECT = "equitylens-prod"
SERVICES = ("api", "mathematics")
REQUIRED_JOBS = {
    "Backend build and test", "Frontend build and test", "Mathematics tests",
    "Eval harness tests", "Browser smoke (mock API, production build)",
    "Risk cross-language and service integration",
}
ROOT = Path(__file__).resolve().parents[2]
COMMAND_LOG = None


class DeploymentError(Exception):
    pass


def require(condition, message):
    if not condition:
        raise DeploymentError(message)


def run(args, *, input=None, stdin=None, output=None, timeout=120):
    # Do not inherit Compose interpolation overrides from the interactive shell.
    env = {k: v for k, v in os.environ.items() if k in {
        "PATH", "HOME", "USER", "SSH_AUTH_SOCK", "DOCKER_CONFIG", "XDG_RUNTIME_DIR",
        "LANG", "LC_ALL", "SSL_CERT_FILE", "SSL_CERT_DIR",
    }}
    env["COMPOSE_DISABLE_ENV_FILE"] = "1"
    if COMMAND_LOG is not None:
        with COMMAND_LOG.open("ab") as log:
            log.write(("\n$ " + shlex.join(args) + "\n").encode())
    result = subprocess.run(args, input=input, stdin=stdin, stdout=output if output is not None else subprocess.PIPE,
                            stderr=subprocess.PIPE, env=env, timeout=timeout, cwd=ROOT)
    # Full diagnostics stay in the private release directory, never in terminal/CI output.
    if COMMAND_LOG is not None:
        with COMMAND_LOG.open("ab") as log:
            log.write(result.stdout or b"")
            log.write(result.stderr or b"")
    # Docker/Git errors can contain resolved config or credentials: don't echo them.
    require(result.returncode == 0, f"{args[0]} command failed (exit {result.returncode}); deployment stopped.")
    return result.stdout or b""


def write_json(path, value):
    temporary = path.with_name(path.name + ".tmp")
    with temporary.open("w") as stream:
        os.fchmod(stream.fileno(), 0o600)
        stream.write(json.dumps(value, indent=2, ensure_ascii=False) + "\n")
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)


def private_directory(path):
    require(not path.is_symlink(), "State directory must not be a symlink.")
    path.mkdir(parents=True, exist_ok=True, mode=0o700)
    require(path.stat().st_uid == os.getuid() and path.stat().st_mode & 0o077 == 0,
            "State directory must be owned by you with mode 0700.")


def github(path):
    request = urllib.request.Request("https://api.github.com/repos/" + REPOSITORY + path,
                                     headers={"Accept": "application/vnd.github+json", "User-Agent": "EquityLens-deploy"})
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if token:
        request.add_header("Authorization", "Bearer " + token)
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def check_origin():
    origin = run(["git", "remote", "get-url", "origin"]).decode().strip()
    # actions/checkout uses HTTPS without .git; accept only these exact identities.
    allowed = {f"{base}{suffix}" for base in (
        f"git@github.com:{REPOSITORY}", f"https://github.com/{REPOSITORY}"
    ) for suffix in ("", ".git")}
    require(origin in allowed, "Unexpected Git origin.")


def check_ci(sha):
    runs = github(f"/actions/workflows/main.yml/runs?head_sha={sha}&event=push&branch=main&per_page=100")["workflow_runs"]
    matches = [r for r in runs if r["head_sha"] == sha and r["head_branch"] == "main" and r["event"] == "push"]
    require(matches, "No main push CI run exists for this exact SHA.")
    latest = max(matches, key=lambda r: r["id"])
    require(latest["status"] == "completed" and latest["conclusion"] == "success", "Latest CI attempt is not green.")
    jobs = github(f"/actions/runs/{latest['id']}/jobs?filter=latest&per_page=100")["jobs"]
    passed = {j["name"] for j in jobs if j["conclusion"] == "success" and j["status"] == "completed"}
    require(REQUIRED_JOBS <= passed, "CI lacks required successful jobs (skipped jobs do not count).")
    return latest["html_url"]


def check_migrations(expected, actual):
    require(expected and len(expected) == len(set(expected)), "Invalid migration inventory.")
    require(set(expected) == set(actual) and len(actual) == len(set(actual)),
            "Database migrations differ from target. Apply/review migrations manually before deployment; no schema changes performed.")


def inventory(source):
    ids = []
    for file in (source / "src/EquityLens.Api/Migrations").glob("*.cs"):
        ids += re.findall(r'\[Migration\("([^"\n]+)"\)\]', file.read_text(encoding="utf-8-sig"))
    return sorted(ids)


def compose(file, *args):
    # Saved configs escape literal dollars so Compose does not reinterpret secrets.
    return ["docker", "compose", "--project-name", PROJECT, "-f", str(file), *args]


def map_strings(value, transform):
    if isinstance(value, str):
        return transform(value)
    if isinstance(value, list):
        return [map_strings(v, transform) for v in value]
    if isinstance(value, dict):
        return {k: map_strings(v, transform) for k, v in value.items()}
    return value


def save_compose(path, config):
    write_json(path, map_strings(config, lambda text: text.replace("$", "$$")))


def resolved_config(file, env_file, override=None):
    command = ["docker", "compose", "--project-name", PROJECT, "--env-file", str(env_file), "-f", str(file)]
    def resolve(extra):
        rendered = json.loads(run(command + extra + ["config", "--format", "json"]))
        # Compose escapes dollars in its output so the document can be loaded again.
        return map_strings(rendered, lambda text: text.replace("$$", "$"))
    base = resolve([])
    if override is None:
        return base
    result = resolve(["-f", str(override)])
    original, changed = copy.deepcopy(base), copy.deepcopy(result)
    for service in SERVICES:
        original["services"][service].pop("environment", None)
        changed["services"][service].pop("environment", None)
    require(original == changed, "Compose override may only change api/mathematics environment values.")
    return result


def validate_api_configuration(source, target):
    # A clean git archive must not depend on ignored developer appsettings.json.
    jwt = {}
    for name in ("appsettings.json", "appsettings.Production.json"):
        path = source / "src/EquityLens.Api" / name
        if path.exists():
            jwt.update(json.loads(path.read_text()).get("Jwt", {}))
    env = target["services"]["api"].get("environment", {})
    jwt.update({k.removeprefix("Jwt__"): v for k, v in env.items() if k.startswith("Jwt__")})
    require(all(isinstance(jwt.get(k), str) and jwt[k].strip() for k in ("Issuer", "Audience", "Key")),
            "Target lacks Jwt Issuer/Audience/Key; supply runtime configuration with --compose-override.")
    require(len(jwt["Key"].encode()) >= 32, "JWT signing key must contain at least 32 bytes.")
    require(str(jwt.get("ExpireMinutes", "")).isdigit() and int(jwt["ExpireMinutes"]) > 0,
            "Target lacks positive Jwt ExpireMinutes; supply runtime configuration with --compose-override.")


def inspect(service):
    result = json.loads(run(["docker", "inspect", f"{PROJECT}-{service}"]))[0]
    labels = result["Config"].get("Labels") or {}
    require(labels.get("com.docker.compose.project") == PROJECT and labels.get("com.docker.compose.service") == service,
            "Container does not belong to the expected Compose project/service.")
    return result


def query_migrations():
    sql = 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
    return run(["docker", "exec", f"{PROJECT}-postgres", "sh", "-c",
                'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "$1"',
                "sh", sql]).decode().splitlines()


def backup(directory):
    dump = directory / "database.dump"
    with dump.open("wb") as output:
        run(["docker", "exec", f"{PROJECT}-postgres", "sh", "-c",
             'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc'], output=output, timeout=1800)
    require(dump.stat().st_size > 0, "Empty database backup.")
    # Decode the whole archive, not just its table of contents; this is not a restore drill.
    with dump.open("rb") as source:
        # Stream input instead of loading a production database dump into RAM.
        run(["docker", "exec", "-i", f"{PROJECT}-postgres", "pg_restore", "--file=/dev/null"],
            stdin=source, output=subprocess.DEVNULL, timeout=1800)
    with dump.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    (directory / "database.sha256").write_text(digest + "  database.dump\n")
    return digest


def check_configs(current, target):
    require(current.get("name") == target.get("name") == PROJECT, "Unexpected Compose project.")
    require(set(current["services"]) == set(target["services"]), "Service set changed; needs a separate infrastructure deployment.")
    for key in set(current) | set(target):
        if key != "services":
            require(current.get(key) == target.get(key), f"Compose {key} changed; manual infrastructure review required.")
    for name in current["services"]:
        if name not in SERVICES:
            require(current["services"][name] == target["services"][name],
                    f"Infrastructure service {name} changed; this script only updates api/mathematics.")
    for name in SERVICES:
        require(target["services"][name].get("container_name") == f"{PROJECT}-{name}", "Unexpected container name.")
        # Avoid bind-mounted code/config and infrastructure changes disguised as app updates.
        for key in ("volumes", "networks", "ports", "depends_on", "privileged", "devices"):
            require(current["services"][name].get(key) == target["services"][name].get(key),
                    f"{name} {key} changed; manual review required.")


def retain_infrastructure_paths(target, source, live_directory):
    # Data services keep their existing host paths; this deployment never restarts them.
    for service in set(target["services"]) - set(SERVICES):
        for volume in target["services"][service].get("volumes", []):
            if volume.get("type") == "bind":
                relative = Path(volume["source"]).relative_to(source)
                volume["source"] = str(live_directory / relative)


def assert_current(config):
    images = {}
    for service in SERVICES:
        state = inspect(service)
        require(state["State"]["Running"], f"Current {service} is not running; recover it manually first.")
        actual = dict(item.split("=", 1) for item in state["Config"]["Env"])
        expected = config["services"][service].get("environment", {})
        require(all(actual.get(k) == str(v) for k, v in expected.items()),
                f"Live {service} environment differs from supplied live Compose/env file; cannot capture a reliable rollback.")
        baseline = json.loads(run(["docker", "image", "inspect", state["Image"]]))[0]
        expected_full = dict(item.split("=", 1) for item in baseline["Config"].get("Env", []))
        expected_full.update({k: str(v) for k, v in expected.items()})
        require(actual == expected_full, f"Live {service} has unrecorded environment overrides; rollback capture refused.")
        images[service] = state["Image"]
    return images


def wait_healthy(images, health_url, timeout):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        states = {name: inspect(name) for name in SERVICES}
        healthy = all(s["State"]["Running"] and s["Image"] == images[n] for n, s in states.items())
        healthy = healthy and states["api"]["State"].get("Health", {}).get("Status") == "healthy"
        if healthy:
            break
        time.sleep(3)
    else:
        raise DeploymentError("Timed out waiting for the expected containers/API health.")
    run(["docker", "exec", f"{PROJECT}-postgres", "sh", "-c",
         'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "SELECT 1"'])
    require(run(["docker", "exec", f"{PROJECT}-redis", "redis-cli", "ping"]).strip() == b"PONG", "Redis unavailable.")
    # Check the worker container's installed engine and its own Redis connection; no queue writes or AI calls.
    probe = ('import os,sys; from redis import Redis; '
             'from equitylens_mathematics.models import RiskBacktestInput; '
             'from equitylens_mathematics.engine import calculate_current_risk; '
             'assert Redis.from_url(os.environ["REDIS_URL"]).ping(); '
             'x=RiskBacktestInput.model_validate_json(sys.stdin.read()); '
             'r=calculate_current_risk(x,"deployment-smoke","risk"); '
             'assert r["outcome"]=="Success"; print("risk-engine-ok")')
    fixture = (ROOT / "tests/fixtures/risk_backtest_input.json").read_bytes()
    require(b"risk-engine-ok" in run(["docker", "exec", "-i", f"{PROJECT}-mathematics", "python", "-c", probe], input=fixture),
            "Mathematics engine smoke failed.")
    internal = run(["docker", "exec", f"{PROJECT}-api", "curl", "--fail", "--silent", "--max-time", "20",
                    "http://127.0.0.1:5034/healthz"])
    require(json.loads(internal).get("status") == "ok", "Direct API health payload is invalid.")
    response = run(["curl", "--fail", "--silent", "--show-error", "--max-time", "20", health_url])
    # Production Caddy handles /healthz itself and returns plain text, not API JSON.
    if response.strip() != b"ok":
        require(json.loads(response).get("status") == "ok", "Public health response is invalid.")
    public = urllib.parse.urlsplit(health_url)
    protected_url = urllib.parse.urlunsplit((public.scheme, public.netloc, "/api/portfolios", "", ""))
    status = run(["curl", "--silent", "--show-error", "--max-time", "20", "--output", "/dev/null",
                  "--write-out", "%{http_code}", protected_url])
    require(status.strip() == b"401", "Public protected API did not return 401 without credentials.")
    for name, before in states.items():
        after = inspect(name)
        require(after["State"]["Running"] and after["Image"] == images[name]
                and after["RestartCount"] == before["RestartCount"], f"{name} stopped/restarted during smoke checks.")


def activate(config, images, args):
    # Stop both producers/consumers before replacing either; never stop data services or remove volumes.
    run(compose(config, "stop", "--timeout", "60", *SERVICES), timeout=150)
    run(compose(config, "up", "-d", "--no-deps", "--no-build", "--pull", "never", *SERVICES), timeout=180)
    wait_healthy(images, args.health_url, args.timeout)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--sha", help="Full 40-character SHA already merged to main with green main push CI")
    mode.add_argument("--rollback", help="Release directory name to restore its previous images/config (no database restore)")
    parser.add_argument("--execute", action="store_true", help="Actually deploy; default only prints a plan")
    parser.add_argument("--env-file", type=Path, help="Explicit production env file; never sourced as shell code")
    parser.add_argument("--live-compose", type=Path, help="Exact Compose file currently used by production")
    parser.add_argument("--compose-override", type=Path, help="Private override restricted to api/mathematics environment values")
    parser.add_argument("--target-compose-override", type=Path, help="Target-only app environment override; defaults to --compose-override")
    parser.add_argument("--state-dir", type=Path, default=Path.home() / ".local/state/equitylens/deployments")
    parser.add_argument("--health-url", default="https://equitylens.duckdns.org/healthz")
    parser.add_argument("--timeout", type=int, default=180)
    args = parser.parse_args(argv)
    if args.sha:
        require(re.fullmatch(r"[0-9a-f]{40}", args.sha), "Use a full lowercase 40-character commit SHA.")
        require(args.env_file and args.live_compose, "--env-file and --live-compose are required for deployment.")
    if args.rollback:
        require(re.fullmatch(r"[0-9TZ-]+-[0-9a-f]{8}", args.rollback), "Invalid release directory name.")
    health = urllib.parse.urlsplit(args.health_url)
    require(health.scheme == "https" and health.hostname and not health.username and not health.password
            and not health.query and not health.fragment and not any(c.isspace() for c in args.health_url),
            "Health URL must use HTTPS with a hostname and no credentials/query/fragment.")
    require(10 <= args.timeout <= 1800, "Timeout must be 10–1800 seconds.")
    print("Target:", args.sha or ("rollback " + args.rollback))
    print("Scope: api + mathematics only; no migrations, no database restore, no deployment of infrastructure.")
    if not args.execute:
        print("PLAN ONLY: no commands, network requests, or production reads were performed.")
        print("Execute checks exact CI/migrations, saves private backup/images/config, updates containers, then runs smoke checks.")
        print("Add --execute to perform this plan. See docs/operations/manual-deployment.md for prerequisites/rollback limits.")
        return
    os.umask(0o077)
    args.state_dir = args.state_dir.absolute()
    require(not args.state_dir.resolve().is_relative_to(ROOT), "Store private deployments outside the repository.")
    private_directory(args.state_dir)
    with (args.state_dir / "deploy.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise DeploymentError("Another deployment holds the lock.") from None
        execute(args)


def execute(args):
    global COMMAND_LOG
    # Prevent accidental deployment through a remote Docker context.
    context = run(["docker", "context", "inspect"]).decode()
    require(json.loads(context)[0]["Endpoints"]["docker"]["Host"].startswith("unix://"), "Use a local Unix Docker context on the production host.")
    for service in ("postgres", "redis"):
        require(inspect(service)["State"]["Running"], f"Required service {service} is not running.")
    release = args.state_dir / (time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + uuid.uuid4().hex[:8])
    release.mkdir(mode=0o700)
    COMMAND_LOG = release / "commands.log"
    manifest = {"status": "preparing", "sha": args.sha, "rollback_of": args.rollback}
    write_json(release / "manifest.json", manifest)
    try:
        if args.rollback:
            previous = args.state_dir / args.rollback
            old = json.loads((previous / "manifest.json").read_text())
            require(old.get("rollback_ready"), "Release has no complete rollback snapshot.")
            expected = old["migrations"]
            check_migrations(expected, query_migrations())
            config = previous / "rollback.compose.json"
            images = old["previous_images"]
            for image in images.values():
                run(["docker", "image", "inspect", image])
            backup(release)
            activate(config, images, args)
        else:
            require(args.env_file.is_file() and not args.env_file.is_symlink(), "Production env file must be a regular file.")
            require(args.env_file.stat().st_mode & 0o077 == 0, "Production env file must not be group/world readable (chmod 600).")
            args.env_file = args.env_file.resolve()
            args.live_compose = args.live_compose.resolve(strict=True)
            override = getattr(args, "compose_override", None)
            target_override = getattr(args, "target_compose_override", None) or override
            for candidate in (override, target_override):
                if candidate is None:
                    continue
                require(candidate.is_file() and not candidate.is_symlink() and candidate.stat().st_mode & 0o077 == 0,
                        "Compose override must be a regular private file (chmod 600).")
            override = override.resolve() if override else None
            target_override = target_override.resolve() if target_override else None
            check_origin()
            run(["git", "fetch", "origin", "main"])
            run(["git", "merge-base", "--is-ancestor", args.sha, "origin/main"])
            manifest["ci_url"] = check_ci(args.sha)
            # git archive excludes ignored/uncommitted appsettings, credentials and build output.
            archive = release / "source.tar"
            with archive.open("wb") as output:
                run(["git", "archive", args.sha], output=output)
            source = release / "source"
            source.mkdir()
            with tarfile.open(archive) as tar:
                tar.extractall(source, filter="data")
            archive.unlink()
            expected = inventory(source)
            check_migrations(expected, query_migrations())
            current = resolved_config(args.live_compose, args.env_file, override)
            target = resolved_config(source / "docker-compose.prod.yml", args.env_file, target_override)
            retain_infrastructure_paths(target, source, args.live_compose.parent)
            check_configs(current, target)
            validate_api_configuration(source, target)
            images_before = assert_current(current)
            rollback = copy.deepcopy(current)
            for service in SERVICES:
                rollback["services"][service].pop("build", None)
                rollback["services"][service]["image"] = images_before[service]
                run(["docker", "tag", images_before[service], f"{PROJECT}-{service}:before-{release.name}"])
                target["services"][service]["image"] = f"{PROJECT}-{service}:release-{release.name}"
                target["services"][service]["build"].setdefault("labels", {})["org.opencontainers.image.revision"] = args.sha
            save_compose(release / "rollback.compose.json", rollback)
            manifest.update(previous_images=images_before, migrations=expected, rollback_ready=True)
            write_json(release / "manifest.json", manifest)
            config = release / "target.compose.json"
            save_compose(config, target)
            print("Building immutable release images; existing services keep running.", flush=True)
            run(compose(config, "build", *SERVICES), timeout=1800)
            images = {s: json.loads(run(["docker", "image", "inspect", target["services"][s]["image"]]))[0]["Id"] for s in SERVICES}
            # Recheck after the build, then a consistent pg_dump while existing services still run.
            check_migrations(expected, query_migrations())
            require(assert_current(current) == images_before, "Running images changed during build; deployment stopped.")
            manifest["backup_sha256"] = backup(release)
            manifest.update(status="activating", images=images)
            write_json(release / "manifest.json", manifest)
            activate(config, images, args)
        manifest.update(status="succeeded", images=images)
        write_json(release / "manifest.json", manifest)
        print("Deployment succeeded. Private record:", release)
    except BaseException:
        manifest["status"] = "failed"
        write_json(release / "manifest.json", manifest)
        print("Deployment stopped. Private record:", release, file=sys.stderr)
        if manifest.get("rollback_ready"):
            command = [str(ROOT / "scripts/deploy/deploy.sh"), "--rollback", release.name,
                       "--state-dir", str(args.state_dir), "--health-url", args.health_url, "--execute"]
            print("After reviewing run/schema compatibility, restore previous images with:\n" + shlex.join(command), file=sys.stderr)
        raise
    finally:
        COMMAND_LOG = None


if __name__ == "__main__":
    try:
        main()
    except (Exception, KeyboardInterrupt) as error:
        # Never leak command output, request headers, or resolved environment in tracebacks.
        print(str(error) if isinstance(error, DeploymentError) else f"Deployment failed ({type(error).__name__}); no automatic rollback.", file=sys.stderr)
        sys.exit(1)
