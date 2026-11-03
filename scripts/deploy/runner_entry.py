"""Runner entrypoint. Verify is read-only; deployment requires an explicit mode."""
import json
import os
from pathlib import Path
import re
import subprocess
import sys

import deploy


def execute(mode, sha, config):
    deploy.require(mode in ("verify", "deploy"), "Unsupported mode")
    deploy.require(re.fullmatch(r"[0-9a-f]{40}", sha or ""), "Expected a full SHA")
    deploy.require(os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch" and
                   os.environ.get("GITHUB_REF") == "refs/heads/main" and
                   os.environ.get("GITHUB_REPOSITORY") == deploy.REPOSITORY and
                   os.environ.get("GITHUB_SHA") == sha, "Manual main workflow context required")
    deploy.require(deploy.run(["git", "rev-parse", "HEAD"]).decode().strip() == sha, "Checkout must match workflow SHA")
    compose = Path(config["live_compose"])
    env = Path(config["env_file"])
    override = Path(config["compose_override"])
    for path in (env, override):
        deploy.require(path.is_file() and not path.is_symlink() and path.stat().st_mode & 0o077 == 0, "Private runtime file required")
    if mode == "deploy":
        deploy.main(["--sha", sha, "--live-compose", str(compose), "--env-file", str(env),
                     "--compose-override", str(override), "--state-dir", config["state_dir"],
                     "--health-url", config["health_url"], "--execute"])
        return
    # No migrations, queue writes, backup, image build, or container restart.
    deploy.check_ci(sha)
    context = json.loads(deploy.run(["docker", "context", "inspect"]))
    deploy.require(context[0]["Endpoints"]["docker"]["Host"].startswith("unix://"), "Local Docker required")
    deploy.check_migrations(deploy.inventory(deploy.ROOT), deploy.query_migrations())
    current = deploy.resolved_config(compose, env, override)
    target = deploy.resolved_config(deploy.ROOT / "docker-compose.prod.yml", env, override)
    deploy.retain_infrastructure_paths(target, deploy.ROOT, compose.resolve().parent)
    deploy.check_configs(current, target)
    deploy.validate_api_configuration(deploy.ROOT, target)
    images = deploy.assert_current(current)
    deploy.wait_healthy(images, config["health_url"], 30)
    print("Read-only runner verification passed: exact main CI, migrations, runtime settings, health and risk engine.")


if __name__ == "__main__":
    os.umask(0o077)
    try:
        execute(sys.argv[1], os.environ.get("GITHUB_SHA"), json.loads(Path("/etc/equitylens/runner-deploy.json").read_text()))
    except (Exception, KeyboardInterrupt) as error:
        raise SystemExit(str(error) if isinstance(error, deploy.DeploymentError) else "Runner operation failed; inspect private deployment records")
