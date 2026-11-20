"""Test DxAudio's actual Unity schedule and required status decision."""

import copy
import os
import subprocess
import unittest
from pathlib import Path

import yaml


WORKFLOW = Path(__file__).resolve().parents[1] / "workflows" / "unity-tests.yml"
FLOOR = "2021.3.45f1"
MODERN = "6000.5.2f1"


def cadence_problems(workflow: dict) -> list[str]:
    problems = []
    if not {"pull_request", "push", "schedule", "workflow_dispatch"} <= set(workflow["on"]):
        problems.append("Unity workflow is missing a validation trigger")
    jobs = workflow["jobs"]
    matrix = jobs["unity-tests"]["strategy"]["matrix"]
    if set(matrix["unity-version"]) != {FLOOR, MODERN}:
        problems.append("Unity matrix must include the floor and normal PR editor")
    excludes = matrix.get("exclude", [])
    expected = f"${{{{ (github.event_name == 'pull_request' || github.event_name == 'push') && '{FLOOR}' || '' }}}}"
    if excludes != [{"unity-version": expected}]:
        problems.append("only PR and push runs may exclude the floor editor")
    for name in ("runner-preflight", "unity-tests"):
        condition = jobs[name]["if"]
        if "dependabot[bot]" not in condition or "github.event.pull_request.head.repo.full_name == github.repository" not in condition:
            problems.append(f"{name} must exclude revision sources without licensed credentials")
    return problems


class UnityWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.workflow = yaml.load(WORKFLOW.read_text(encoding="utf-8"), Loader=yaml.BaseLoader)

    def test_cadence_and_planted_missing_floor(self):
        self.assertEqual([], cadence_problems(self.workflow))
        changed = copy.deepcopy(self.workflow)
        changed["jobs"]["unity-tests"]["strategy"]["matrix"]["unity-version"].remove(FLOOR)
        self.assertTrue(cadence_problems(changed))

    def test_licensed_job_preserves_its_step_budgets_and_cleanup(self):
        job = self.workflow["jobs"]["unity-tests"]
        minutes = [int(step.get("timeout-minutes", job["timeout-minutes"])) for step in job["steps"]]
        self.assertGreater(int(job["timeout-minutes"]), sum(minutes))

    def test_required_status_uses_real_job_outcomes(self):
        job = self.workflow["jobs"]["unity-ci-success"]
        self.assertEqual("Unity CI Success", job["name"])
        self.assertIn("always()", job["if"])
        self.assertCountEqual(["runner-preflight", "unity-tests"], job["needs"])
        late_head = next(step for step in job["steps"] if step["name"] == "Require current PR head before reporting success")
        self.assertIn("/require-current-pr-head@", late_head["uses"])
        self.assertEqual("${{ github.event.pull_request.head.sha }}", late_head["with"]["expected-head-sha"])
        command = next(step["run"] for step in job["steps"] if step["name"] == "Verify Unity CI result shape")
        for preflight, unity, fork, dependabot, expected in (
            ("success", "success", "false", "false", 0),
            ("success", "failure", "false", "false", 1),
            ("failure", "skipped", "false", "false", 1),
            ("success", "skipped", "false", "false", 1),
            ("success", "cancelled", "false", "false", 1),
            ("skipped", "skipped", "true", "false", 0),
            ("skipped", "skipped", "false", "true", 0),
            ("success", "skipped", "true", "false", 1),
        ):
            with self.subTest(preflight=preflight, unity=unity, fork=fork, dependabot=dependabot):
                environment = {
                    **os.environ,
                    "RUNNER_PREFLIGHT_RESULT": preflight,
                    "UNITY_TESTS_RESULT": unity,
                    "FORK_PR": fork,
                    "DEPENDABOT_PR": dependabot,
                }
                result = subprocess.run(["bash", "-e", "-c", command], env=environment)
                self.assertEqual(expected, result.returncode)


if __name__ == "__main__":
    unittest.main()
