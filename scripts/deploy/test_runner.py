import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import deploy
import runner_entry

spec = importlib.util.spec_from_file_location('environment_check', Path(__file__).parent / 'runner/check_environment.py')
environment_check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(environment_check)
SHA = 'a' * 40
CONTEXT = {'GITHUB_EVENT_NAME': 'workflow_dispatch', 'GITHUB_REF': 'refs/heads/main',
           'GITHUB_REPOSITORY': 'zhao199712/EquityLens', 'GITHUB_SHA': SHA,
           'GITHUB_WORKFLOW_REF': 'zhao199712/EquityLens/.github/workflows/deploy.yml@refs/heads/main'}


class RunnerTests(unittest.TestCase):
    def test_hook_accepts_only_exact_main_manual_workflow(self):
        script = Path(__file__).parent / 'runner/guard.sh'
        for changes, expected in [({}, 0), ({'GITHUB_EVENT_NAME': 'pull_request'}, 1),
                                  ({'GITHUB_EVENT_NAME': 'push'}, 1), ({'GITHUB_REF': 'refs/heads/dev'}, 1),
                                  ({'GITHUB_REPOSITORY': 'attacker/fork'}, 1),
                                  ({'GITHUB_WORKFLOW_REF': 'zhao199712/EquityLens/.github/workflows/main.yml@refs/heads/main'}, 1),
                                  ({'GITHUB_WORKFLOW_REF': ''}, 1)]:
            with self.subTest(changes=changes):
                result = subprocess.run(['bash', str(script)], env={'PATH': os.environ['PATH'], **CONTEXT, **changes}, capture_output=True)
                self.assertEqual(expected, result.returncode)

    def test_environment_requires_review_and_only_main_branch(self):
        env = {'protection_rules': [{'type': 'required_reviewers', 'reviewers': [{'type': 'User'}]}],
               'deployment_branch_policy': {'protected_branches': False, 'custom_branch_policies': True}}
        policies = {'total_count': 1, 'branch_policies': [{'name': 'main', 'type': 'branch'}]}
        environment_check.validate(env, policies)
        for broken_env, broken_policies in [({}, policies), ({**env, 'protection_rules': []}, policies),
                ({**env, 'deployment_branch_policy': None}, policies),
                (env, {'total_count': 2, 'branch_policies': policies['branch_policies']}),
                (env, {'total_count': 1, 'branch_policies': [{'name': 'main', 'type': 'tag'}]}),
                (env, {'total_count': 1, 'branch_policies': [{'name': '*', 'type': 'branch'}]})]:
            with self.assertRaises(ValueError):
                environment_check.validate(broken_env, broken_policies)

    def test_runner_rejects_wrong_context_or_checkout_before_deploy(self):
        for changes, head in [({'GITHUB_EVENT_NAME': 'pull_request'}, SHA), ({'GITHUB_REF': 'refs/heads/dev'}, SHA),
                              ({'GITHUB_SHA': 'b'*40}, SHA), ({}, 'b'*40)]:
            with patch.dict(os.environ, {**CONTEXT, **changes}), patch.object(deploy, 'run', return_value=head.encode()), patch.object(deploy, 'main') as main:
                with self.assertRaises(deploy.DeploymentError):
                    runner_entry.execute('deploy', SHA, {})
                main.assert_not_called()

    def test_only_deploy_mode_can_activate(self):
        with tempfile.TemporaryDirectory() as temp:
            file = Path(temp) / 'private'; file.write_text('{}'); file.chmod(0o600)
            config = {k: str(file) for k in ('live_compose','env_file','compose_override','state_dir')}
            config['health_url'] = 'https://example.invalid/healthz'
            with patch.dict(os.environ, CONTEXT), patch.object(deploy, 'run', return_value=SHA.encode()), patch.object(deploy, 'main') as main:
                runner_entry.execute('deploy', SHA, config)
                self.assertIn('--execute', main.call_args.args[0])
                main.reset_mock()
                with patch.object(deploy, 'check_ci', side_effect=deploy.DeploymentError('CI not green')):
                    with self.assertRaises(deploy.DeploymentError):
                        runner_entry.execute('verify', SHA, config)
                main.assert_not_called()
                with self.assertRaises(deploy.DeploymentError):
                    runner_entry.execute('unknown', SHA, config)
                main.assert_not_called()
