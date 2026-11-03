"""Read-only GitHub-hosted gate before assigning any work to production."""
import json
import os
import urllib.request

REPO = "zhao199712/EquityLens"


def validate(environment, policies):
    rules = environment.get("protection_rules", [])
    if not any(r.get("type") == "required_reviewers" and r.get("reviewers") for r in rules):
        raise ValueError("production must have a required reviewer")
    scope = environment.get("deployment_branch_policy") or {}
    if scope.get("protected_branches") or not scope.get("custom_branch_policies"):
        raise ValueError("production must use a custom main-only deployment policy")
    branches = policies.get("branch_policies", [])
    if policies.get("total_count") != 1 or len(branches) != 1 or branches[0].get("name") != "main" or branches[0].get("type") != "branch":
        raise ValueError("production must allow exactly the main branch, with no tags")


def fetch(path):
    request = urllib.request.Request("https://api.github.com/repos/" + REPO + path,
        headers={"Authorization": "Bearer " + os.environ["GH_TOKEN"], "Accept": "application/vnd.github+json",
                 "User-Agent": "EquityLens-deploy-environment-check"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


if __name__ == "__main__":
    try:
        validate(fetch("/environments/production"), fetch("/environments/production/deployment-branch-policies"))
    except Exception as error:
        # No response bodies, headers or tokens in public workflow logs.
        raise SystemExit(str(error) if isinstance(error, ValueError) else "Cannot verify production environment protection")
    print("Required reviewer and main-only deployment environment verified.")
