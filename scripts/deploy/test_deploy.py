"""Failure-path tests: production Docker, databases and GitHub are never contacted."""
import argparse
import contextlib
import copy
import io
import fcntl
import json
import os
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch

import deploy

SHA = "a" * 40
IMAGES = {"api": "sha256:old-api", "mathematics": "sha256:old-math"}


def config():
    return {"name": deploy.PROJECT, "services": {
        "api": {"container_name": "equitylens-prod-api", "build": {"context": "/source/api"}, "environment": {"PASSWORD": "a$b", "Jwt__Issuer": "drill", "Jwt__Audience": "drill", "Jwt__ExpireMinutes": "60", "Jwt__Key": "x" * 32}},
        "mathematics": {"container_name": "equitylens-prod-mathematics", "build": {"context": "/source/math"}},
        "postgres": {"image": "postgres", "volumes": [{"type": "volume", "source": "data", "target": "/var/lib/postgresql"}]},
        "redis": {"image": "redis"},
    }, "volumes": {"data": {"name": "equitylens-prod_data"}}}


class GuardTests(unittest.TestCase):
    def test_origin_accepts_exact_repo_with_optional_suffix_only(self):
        for base in ("git@github.com:zhao199712/EquityLens", "https://github.com/zhao199712/EquityLens"):
            for suffix in ("", ".git"):
                with patch.object(deploy, "run", return_value=(base + suffix).encode()):
                    deploy.check_origin()
        for origin in ("https://github.com/attacker/EquityLens", "https://github.com/zhao199712/EquityLens-other",
                       "https://github.com.example/zhao199712/EquityLens", "https://github.com/zhao199712/EquityLens?x=1",
                       "https://token@github.com/zhao199712/EquityLens", "/tmp/EquityLens"):
            with self.subTest(origin=origin), patch.object(deploy, "run", return_value=origin.encode()), self.assertRaises(deploy.DeploymentError):
                deploy.check_origin()

    def test_inventory_includes_data_only_migrations_without_designer(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp)
            migrations = source / "src/EquityLens.Api/Migrations"
            migrations.mkdir(parents=True)
            (migrations / "001_Schema.Designer.cs").write_text('[Migration("001_Schema")]')
            (migrations / "002_Data.cs").write_text('[Migration("002_Data")]')
            self.assertEqual(["001_Schema", "002_Data"], deploy.inventory(source))

    def test_plan_has_no_side_effects(self):
        with patch.object(deploy, "run") as command, patch.object(deploy, "github") as api, patch.object(deploy, "execute") as execute:
            with contextlib.redirect_stdout(io.StringIO()):
                deploy.main(["--sha", SHA, "--env-file", "/absent", "--live-compose", "/absent"])
            command.assert_not_called()
            api.assert_not_called()
            execute.assert_not_called()

    def test_busy_lock_prevents_execution(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            with (directory / "deploy.lock").open("a") as lock:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                with patch.object(deploy, "execute") as execute, contextlib.redirect_stdout(io.StringIO()):
                    with self.assertRaisesRegex(deploy.DeploymentError, "holds the lock"):
                        deploy.main(["--sha", SHA, "--env-file", "/absent", "--live-compose", "/absent",
                                     "--state-dir", temp, "--execute"])
                    execute.assert_not_called()

    def test_migrations_reject_missing_extra_empty_and_duplicates(self):
        deploy.check_migrations(["001", "002"], ["002", "001"])
        for expected, actual in [([], []), (["001"], []), (["001"], ["001", "002"]), (["001", "001"], ["001"]), (["001"], ["001", "001"])]:
            with self.subTest(expected=expected, actual=actual), self.assertRaises(deploy.DeploymentError):
                deploy.check_migrations(expected, actual)

    def test_ci_checks_exact_sha_main_latest_attempt_and_all_jobs(self):
        green = {"id": 1, "head_sha": SHA, "event": "push", "head_branch": "main", "status": "completed", "conclusion": "success", "html_url": "ci/1"}
        jobs = [{"name": name, "status": "completed", "conclusion": "success"} for name in deploy.REQUIRED_JOBS]
        with patch.object(deploy, "github", side_effect=[{"workflow_runs": [green]}, {"jobs": jobs}]):
            self.assertEqual("ci/1", deploy.check_ci(SHA))
        for modified in [dict(green, head_sha="b"*40), dict(green, event="pull_request"), dict(green, head_branch="feature"), dict(green, status="in_progress"), dict(green, conclusion="failure")]:
            with self.subTest(modified=modified), patch.object(deploy, "github", return_value={"workflow_runs": [modified]}), self.assertRaises(deploy.DeploymentError):
                deploy.check_ci(SHA)
        with patch.object(deploy, "github", return_value={"workflow_runs": [green, dict(green, id=2, conclusion="failure")]}), self.assertRaises(deploy.DeploymentError):
            deploy.check_ci(SHA)
        with patch.object(deploy, "github", side_effect=[{"workflow_runs": [green]}, {"jobs": jobs[:-1]}]), self.assertRaises(deploy.DeploymentError):
            deploy.check_ci(SHA)
        with patch.object(deploy, "github", side_effect=[{"workflow_runs": [green]}, {"jobs": [dict(j, conclusion="skipped") for j in jobs]}]), self.assertRaises(deploy.DeploymentError):
            deploy.check_ci(SHA)

    def test_infrastructure_and_app_mount_changes_blocked(self):
        original = config()
        deploy.check_configs(original, copy.deepcopy(original))
        for mutate in [lambda c: c["services"]["postgres"].update(image="new"),
                       lambda c: c["services"]["api"].update(volumes=["/host:/app"]),
                       lambda c: c["volumes"].update(other={}),
                       lambda c: c["services"]["api"].update(container_name="other")]:
            changed = copy.deepcopy(original)
            mutate(changed)
            with self.assertRaises(deploy.DeploymentError):
                deploy.check_configs(original, changed)

    def test_shell_overrides_and_errors_not_forwarded(self):
        result = argparse.Namespace(returncode=1, stdout=b"secret", stderr=b"PASSWORD=secret")
        with patch.dict(os.environ, {"POSTGRES_PASSWORD": "override", "DOCKER_HOST": "tcp://wrong", "RESEARCH_ENGINE": "bad"}), patch("subprocess.run", return_value=result) as call:
            with self.assertRaisesRegex(deploy.DeploymentError, "exit 1") as caught:
                deploy.run(["docker", "compose", "config"])
            self.assertNotIn("secret", str(caught.exception))
            self.assertNotIn("POSTGRES_PASSWORD", call.call_args.kwargs["env"])
            self.assertNotIn("DOCKER_HOST", call.call_args.kwargs["env"])

    def test_literal_dollars_and_private_config(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "compose.json"
            deploy.save_compose(path, config())
            saved = json.loads(path.read_text())
            self.assertEqual("a$$b", saved["services"]["api"]["environment"]["PASSWORD"])
            self.assertEqual(0o600, path.stat().st_mode & 0o777)

    def test_saved_compose_roundtrip_with_real_cli_no_daemon(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "compose.json"
            deploy.save_compose(path, config())
            rendered = deploy.resolved_config(path, Path("/dev/null"))
            self.assertEqual("a$b", rendered["services"]["api"]["environment"]["PASSWORD"])
            self.assertEqual("equitylens-prod_data", rendered["volumes"]["data"]["name"])

    def test_repository_compose_roundtrip_offline(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp)
            env = source / "env"
            # Synthetic credentials; never load a developer/production env file.
            env.write_text("POSTGRES_PASSWORD='dummy$pass'\nS3_SECRET_ACCESS_KEY='dummy'\nJWT_KEY='dummy'\n")
            filename = "docker-compose.prod.yml"
            (source / filename).write_bytes((deploy.ROOT / filename).read_bytes())
            current = deploy.resolved_config(deploy.ROOT / filename, env)
            target = deploy.resolved_config(source / filename, env)
            deploy.retain_infrastructure_paths(target, source, deploy.ROOT)
            deploy.check_configs(current, target)
            self.assertIn("dummy$pass", target["services"]["api"]["environment"]["ConnectionStrings__PostgreSQL"])
            saved = source / "frozen.json"
            deploy.save_compose(saved, target)
            again = deploy.resolved_config(saved, Path("/dev/null"))
            self.assertEqual(target["services"]["api"]["environment"], again["services"]["api"]["environment"])

    def test_clean_archive_requires_complete_jwt_configuration(self):
        with tempfile.TemporaryDirectory() as temp:
            target = config()
            deploy.validate_api_configuration(Path(temp), target)
            for key in ("Jwt__Key", "Jwt__ExpireMinutes", "Jwt__Issuer", "Jwt__Audience"):
                broken = copy.deepcopy(target)
                del broken["services"]["api"]["environment"][key]
                with self.subTest(key=key), self.assertRaises(deploy.DeploymentError):
                    deploy.validate_api_configuration(Path(temp), broken)

    def test_runtime_override_only_accepts_app_environment_real_cli(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "base.json"
            override = Path(temp) / "override.json"
            deploy.save_compose(source, config())
            override.write_text(json.dumps({"services":{"api":{"environment":{"Jwt__ExpireMinutes":"120"}}}}))
            result = deploy.resolved_config(source, Path("/dev/null"), override)
            self.assertEqual("120", result["services"]["api"]["environment"]["Jwt__ExpireMinutes"])
            for change in ({"api":{"image":"unreviewed"}}, {"postgres":{"environment":{"SECRET":"changed"}}}):
                override.write_text(json.dumps({"services": change}))
                with self.assertRaises(deploy.DeploymentError):
                    deploy.resolved_config(source, Path("/dev/null"), override)

    def test_rollback_path_and_sha_validation(self):
        for args in [["--rollback", "../../other"], ["--sha", "main"], ["--sha", SHA]]:
            with self.assertRaises(deploy.DeploymentError):
                deploy.main(args)

    def test_smoke_checks_engine_and_dependencies_and_fails_closed(self):
        states = {s: {"Image": IMAGES[s], "State": {"Running": True, "Health": {"Status": "healthy"}}, "RestartCount": 0} for s in deploy.SERVICES}
        for failure in (None, "redis", "engine", "http"):
            calls = []
            def command(args, **kwargs):
                calls.append(args)
                if "redis-cli" in args:
                    return b"bad" if failure == "redis" else b"PONG"
                if "python" in args:
                    compile(args[-1], "smoke", "exec")
                    self.assertIn(b"portfolioId", kwargs["input"])
                    return b"bad" if failure == "engine" else b"risk-engine-ok"
                if "http://127.0.0.1:5034/healthz" in args:
                    return b'{"status":"ok"}'
                if "%{http_code}" in args:
                    return b"401"
                if args[0] == "curl":
                    return b'{"status":"bad"}' if failure == "http" else b'{"status":"ok"}'
                return b"1"
            with self.subTest(failure=failure), patch.object(deploy, "inspect", side_effect=lambda s: states[s]), patch.object(deploy, "run", side_effect=command):
                if failure:
                    with self.assertRaises(deploy.DeploymentError):
                        deploy.wait_healthy(IMAGES, "https://example.invalid/healthz", 10)
                else:
                    deploy.wait_healthy(IMAGES, "https://example.invalid/healthz", 10)
                    self.assertTrue(any("psql" in " ".join(c) for c in calls))
                    self.assertTrue(any("python" in c for c in calls))

    def test_caddy_plaintext_health_also_requires_api_and_auth_boundary(self):
        state = {"Image": "sha256:expected", "State": {"Running": True, "Health": {"Status": "healthy"}}, "RestartCount": 0}
        for auth_status in (b"401", b"200"):
            calls = []
            def command(args, **kwargs):
                calls.append(args)
                if "redis-cli" in args:
                    return b"PONG"
                if "python" in args:
                    return b"risk-engine-ok"
                if "http://127.0.0.1:5034/healthz" in args:
                    return b'{"status":"ok"}'
                if "%{http_code}" in args:
                    return auth_status
                if args[0] == "curl":
                    return b"ok"
                return b"1"
            with self.subTest(auth_status=auth_status), patch.object(deploy, "inspect", return_value=state), patch.object(deploy, "run", side_effect=command):
                if auth_status == b"401":
                    deploy.wait_healthy({s:"sha256:expected" for s in deploy.SERVICES}, "https://example.invalid/healthz", 10)
                    self.assertTrue(any("http://127.0.0.1:5034/healthz" in c for c in calls))
                    self.assertTrue(any("https://example.invalid/api/portfolios" in c for c in calls))
                else:
                    with self.assertRaises(deploy.DeploymentError):
                        deploy.wait_healthy({s:"sha256:expected" for s in deploy.SERVICES}, "https://example.invalid/healthz", 10)

    def test_wrong_image_cannot_pass_health(self):
        state = {"Image": "wrong", "State": {"Running": True, "Health": {"Status": "healthy"}}}
        with patch.object(deploy, "inspect", return_value=state), patch.object(deploy.time, "monotonic", side_effect=[0, 1, 11]), patch.object(deploy.time, "sleep"), patch.object(deploy, "run") as run:
            with self.assertRaisesRegex(deploy.DeploymentError, "Timed out"):
                deploy.wait_healthy(IMAGES, "https://example.invalid/healthz", 10)
            run.assert_not_called()

    def test_live_extra_environment_is_rejected(self):
        current = config()
        state = {"State": {"Running": True}, "Image": "image", "Config": {"Env": ["PASSWORD=a$b", "UNTRACKED=secret"]}}
        with patch.object(deploy, "inspect", return_value=state), patch.object(deploy, "run", return_value=b'[{"Config":{"Env":[]}}]'), self.assertRaises(deploy.DeploymentError):
            deploy.assert_current(current)


class LifecycleTests(unittest.TestCase):
    def exercise(self, failure=None, rollback=False, separate_overrides=False, origin=None):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            env = directory / "env"
            env.write_text("PASSWORD='a$b'\n")
            env.chmod(0o600)
            live = directory / "live.yml"
            live.write_text("services: {}")
            args = argparse.Namespace(state_dir=directory, sha=None if rollback else SHA, rollback=None,
                                      env_file=env, live_compose=live, health_url="https://example.invalid/healthz", timeout=10)
            if separate_overrides:
                args.compose_override = directory / "current.json"
                args.target_compose_override = directory / "target.json"
                for path in (args.compose_override, args.target_compose_override):
                    path.write_text('{}')
                    path.chmod(0o600)
            resolved_calls = []
            def resolve(file, env_file, override=None):
                resolved_calls.append(override)
                result = config()
                result["services"]["api"]["environment"]["PHASE"] = "target" if file != live else "current"
                return result
            def assert_current(current):
                self.assertEqual("current", current["services"]["api"]["environment"]["PHASE"])
                return IMAGES
            if rollback:
                args.rollback = "20260101T000000Z-12345678"
                previous = directory / args.rollback
                previous.mkdir()
                deploy.write_json(previous / "manifest.json", {"rollback_ready": True, "migrations": ["001"], "previous_images": IMAGES})
                deploy.save_compose(previous / "rollback.compose.json", config())
            events = []
            def command(argv, **kwargs):
                events.append(tuple(argv))
                if argv[:3] == ["docker", "context", "inspect"]:
                    return b'[{"Endpoints":{"docker":{"Host":"unix:///var/run/docker.sock"}}}]'
                if argv[:3] == ["git", "remote", "get-url"]:
                    return (origin or "git@github.com:zhao199712/EquityLens.git").encode()
                if argv[:2] == ["git", "archive"]:
                    with tarfile.open(fileobj=kwargs["output"], mode="w"):
                        pass
                if argv[:3] == ["docker", "image", "inspect"]:
                    return b'[{"Id":"sha256:new"}]'
                if "build" in argv and failure == "build":
                    raise deploy.DeploymentError("build failed")
                return b""
            def backup(_):
                events.append(("BACKUP",))
                if failure == "backup":
                    raise deploy.DeploymentError("backup failed")
                return "digest"
            def smoke(*_):
                events.append(("SMOKE",))
                if failure == "health":
                    raise deploy.DeploymentError("health failed")
            with contextlib.ExitStack() as stack:
                for name, replacement in {
                    "run": command, "inspect": lambda _: {"State": {"Running": True}},
                    "check_ci": lambda _: (_ for _ in ()).throw(deploy.DeploymentError("CI failed")) if failure == "ci" else "ci/1",
                    "inventory": lambda _: ["001"], "query_migrations": lambda: ["002"] if failure == "migration" else ["001"],
                    "resolved_config": resolve, "assert_current": assert_current,
                    "backup": backup, "wait_healthy": smoke,
                }.items():
                    stack.enter_context(patch.object(deploy, name, replacement))
                stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
                stack.enter_context(contextlib.redirect_stderr(io.StringIO()))
                if failure:
                    with self.assertRaises(deploy.DeploymentError):
                        deploy.execute(args)
                else:
                    deploy.execute(args)
            manifests = [json.loads(p.read_text()) for p in directory.glob("*/manifest.json") if p.parent.name != args.rollback]
            self.assertEqual(1, len(manifests))
            self.assertEqual("failed" if failure else "succeeded", manifests[0]["status"])
            if separate_overrides and not failure:
                self.assertEqual([args.compose_override, args.target_compose_override], resolved_calls)
                release = next(p.parent for p in directory.glob("*/manifest.json"))
                for filename, phase in [("rollback.compose.json", "current"), ("target.compose.json", "target")]:
                    saved = json.loads((release / filename).read_text())
                    self.assertEqual(phase, saved["services"]["api"]["environment"]["PHASE"])
            return events

    def test_target_configuration_does_not_change_rollback_capture(self):
        self.exercise(separate_overrides=True)

    def test_success_backup_precedes_stop_and_only_apps_updated(self):
        events = self.exercise()
        stop = next(e for e in events if "stop" in e)
        up = next(e for e in events if "up" in e)
        self.assertLess(events.index(("BACKUP",)), events.index(stop))
        self.assertEqual(("api", "mathematics"), stop[-2:])
        self.assertEqual(("api", "mathematics"), up[-2:])
        for flag in ("--no-deps", "--no-build", "never"):
            self.assertIn(flag, up)
        self.assertEqual(("SMOKE",), events[-1])

    def test_actions_checkout_https_origin_without_suffix_deploys(self):
        events = self.exercise(origin="https://github.com/zhao199712/EquityLens")
        self.assertEqual(("SMOKE",), events[-1])

    def test_preflight_build_and_backup_failures_never_stop_services(self):
        for failure in ("ci", "migration", "build", "backup"):
            with self.subTest(failure=failure):
                events = self.exercise(failure)
                self.assertFalse(any("stop" in e or "up" in e for e in events))

    def test_failed_health_does_not_automatically_restore_database_or_images(self):
        events = self.exercise("health")
        self.assertEqual(1, sum("up" in e for e in events))
        self.assertFalse(any("down" in e or "database" in e for e in events))

    def test_rollback_uses_saved_images_no_build_no_git(self):
        events = self.exercise(rollback=True)
        self.assertFalse(any(e[0] == "git" or "build" in e for e in events))
        inspected = [e[-1] for e in events if e[:3] == ("docker", "image", "inspect")]
        self.assertEqual(set(IMAGES.values()), set(inspected))
        self.assertIn(("BACKUP",), events)
        up = next(e for e in events if "up" in e)
        self.assertTrue(any("rollback.compose.json" in part for part in up))

    def test_rollback_schema_mismatch_blocks_stop(self):
        events = self.exercise("migration", rollback=True)
        self.assertFalse(any("stop" in e or "up" in e for e in events))


if __name__ == "__main__":
    unittest.main()
