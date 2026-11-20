"""Negative controls for the pinned coverage audit."""

from copy import deepcopy
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase, main

from unity_helpers_coverage import action_pin_violations, audit, is_support_operation


class CoverageTests(TestCase):
    def setUp(self) -> None:
        scratch = TemporaryDirectory()
        self.addCleanup(scratch.cleanup)
        self.root = Path(scratch.name)
        self.workflow = self.root / ".github/workflows/ci.yml"
        self.workflow.parent.mkdir(parents=True)
        self.workflow.write_text(
            "jobs:\n  check:\n    steps:\n"
            "      - uses: actions/checkout@" + "a" * 40 + "\n"
            "      - run: |\n          python3 gate.py\n",
            encoding="utf-8",
        )
        (self.root / "gate.py").touch()
        self.inventory = {"revision": "pin", "checks": {"lint": ["unsafe"]}}
        self.coverage = {
            "revision": "pin",
            "gates": {
                "gate": {"path": ".github/workflows/ci.yml", "job": "check", "run": "python3 gate.py", "files": ["gate.py"]}
            },
            "mappings": [
                {"status": "enforced", "gate": "gate", "evidence": "test.py", "checks": {"lint": ["unsafe"]}}
            ],
        }

    def test_coverage_requires_both_a_live_gate_and_a_mapping(self) -> None:
        errors, pending, totals = audit(self.root, self.inventory, self.coverage)
        self.assertEqual(errors, [])
        self.assertEqual(pending, set())
        self.assertEqual(totals["enforced"], 1)

        self.workflow.write_text("jobs:\n  check:\n    steps:\n      - run: echo skipped\n", encoding="utf-8")
        errors, _, _ = audit(self.root, self.inventory, self.coverage)
        self.assertIn("is absent", errors[0])

        coverage = deepcopy(self.coverage)
        coverage["mappings"].clear()
        errors, pending, _ = audit(self.root, self.inventory, coverage)
        self.assertEqual(pending, {("lint", "unsafe")})
        self.assertIn("upstream check lint/unsafe has no decision", errors)
        self.inventory["checks"]["lint"].append("new-check")
        errors, pending, _ = audit(self.root, self.inventory, self.coverage)
        self.assertEqual(pending, {("lint", "new-check")})
        self.assertIn("upstream check lint/new-check has no decision", errors)

    def test_disabled_steps_do_not_prove_a_gate(self) -> None:
        for guard in ("if: false", "continue-on-error: true"):
            with self.subTest(guard=guard):
                self.workflow.write_text(
                    f"jobs:\n  check:\n    steps:\n      - {guard}\n        run: python3 gate.py\n",
                    encoding="utf-8",
                )
                errors, _, _ = audit(self.root, self.inventory, self.coverage)
                self.assertIn("is absent", errors[0])

    def test_pin_check_catches_tags_in_both_step_forms(self) -> None:
        self.assertEqual(action_pin_violations(self.root), [])
        self.workflow.write_text(
            "jobs:\n  check:\n    steps:\n"
            "      - uses: actions/checkout@v4\n"
            "      - name: Setup\n        uses: 'actions/setup-dotnet@main'\n"
            "      - uses: ./local\n",
            encoding="utf-8",
        )
        self.assertEqual(len(action_pin_violations(self.root)), 2)

    def test_workflow_inventory_distinguishes_checks_from_operations(self) -> None:
        self.assertTrue(is_support_operation({"name": "Checkout package sources", "uses": "actions/checkout@v4"}))
        self.assertTrue(is_support_operation({"name": "Upload docs", "uses": "actions/upload-artifact@v4"}))
        self.assertFalse(is_support_operation({"name": "Validate publication manifest", "run": "python3 validate.py"}))
        self.assertFalse(is_support_operation({"name": "Build and test Core", "run": "dotnet test"}))


if __name__ == "__main__":
    main()
