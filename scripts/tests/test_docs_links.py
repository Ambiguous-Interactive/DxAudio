#!/usr/bin/env python3
"""Documentation-link and sample-reference lint contract tests.

The lint must fail exactly when a reader would hit a dead end: a relative
link to a path the repository does not commit, a fragment that names no
heading, or a sample scene that points at a GUID no committed ``.meta``
owns. External URLs, site-absolute paths, links inside fenced code, and
Unity's built-in resource GUIDs must pass silently. Every case drives the
real lint as a subprocess against its own throwaway tree.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    find_python,
    new_temp_root,
    remove_temp_root,
    run_cases,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
LINT = TESTS_DIR.parent / "lint_docs_links.py"

GUID = "a" * 32


class LintSandbox:
    """A throwaway repository tree the lint scans through --repo-root."""

    def __init__(self) -> None:
        self.root = new_temp_root(prefix="docs-links-test-")
        self.last_exit = -1
        self.output = ""

    def write(self, relative: str, content: str) -> None:
        write_fixture_file(self.root, relative, content)

    def run(self) -> None:
        self.output, self.last_exit = run_tool_output(
            [find_python(), str(LINT), "--repo-root", str(self.root)]
        )

    def destroy(self) -> None:
        remove_temp_root(self.root)


def seed_manual(sandbox: LintSandbox) -> None:
    """A valid manual: a page, a heading, a cross-file anchor, and a README."""
    sandbox.write("README.md", "# Package\n\n## Clip lifetime\n\nSee [the manual](Documentation~/index.md).\n")
    sandbox.write(
        "Documentation~/index.md",
        "# Manual\n\n## Install\n\nSee [clip lifetime](../README.md#clip-lifetime) and [Cues](cues.md).\n",
    )
    sandbox.write("Documentation~/cues.md", "# Cues\n\nBack to [the manual](index.md).\n")


def case_valid_manual_passes() -> None:
    sandbox = LintSandbox()
    try:
        seed_manual(sandbox)
        sandbox.run()
        assert_exit_code(0, f"a resolving manual must pass: {sandbox.output}", sandbox.last_exit)
        assert_true("[docs-links] ok" in sandbox.output, "the pass line reports the scan result")
    finally:
        sandbox.destroy()


def case_missing_page_fails() -> None:
    sandbox = LintSandbox()
    try:
        seed_manual(sandbox)
        sandbox.write("Documentation~/index.md", "# Manual\n\nSee [Gone](gone.md).\n")
        sandbox.run()
        assert_exit_code(1, "a link to an uncommitted page must fail", sandbox.last_exit)
        assert_true("Documentation~/index.md" in sandbox.output, "the failure names the linking page")
        assert_true("gone.md" in sandbox.output, "the failure names the missing target")
    finally:
        sandbox.destroy()


def case_missing_anchor_fails() -> None:
    sandbox = LintSandbox()
    try:
        seed_manual(sandbox)
        sandbox.write("README.md", "# Package\n\n## Something else\n")
        sandbox.run()
        assert_exit_code(1, "a fragment that names no heading must fail", sandbox.last_exit)
        assert_true("clip-lifetime" in sandbox.output, "the failure names the missing anchor")
    finally:
        sandbox.destroy()


def case_duplicate_heading_anchors_are_numbered() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write(
            "Documentation~/index.md",
            "# Manual\n\n## Notes\n\n## Notes\n\nSee [second](../README.md#notes-1).\n",
        )
        sandbox.write("README.md", "# Package\n\n## Notes\n\n## Notes\n")
        sandbox.run()
        assert_exit_code(0, "GitHub numbers a repeated heading, so #notes-1 must resolve", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_external_and_code_links_are_skipped() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write(
            "Documentation~/index.md",
            "# Manual\n\n"
            "[Unity](https://unity.com), [mail](mailto:dev@example.com), [site](/manual/start.md).\n\n"
            "```markdown\n[example](not-a-real-file.md)\n```\n",
        )
        sandbox.run()
        assert_exit_code(0, "external, absolute, and example links need no file", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_sample_scene_reference_must_resolve() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write("Samples~/Demo/README.md", "# Demo\n")
        sandbox.write("Samples~/Demo/Demo.unity", f"--- !u!114\n  m_Script: {{guid: {GUID}, type: 3}}\n")
        sandbox.run()
        assert_exit_code(1, "a scene GUID with no companion must fail", sandbox.last_exit)
        assert_true(GUID in sandbox.output, "the failure names the unknown GUID")

        sandbox.write("Runtime/Players.cs.meta", f"fileFormatVersion: 2\nguid: {GUID}\nMonoImporter:\n")
        sandbox.run()
        assert_exit_code(0, "a scene GUID a committed meta owns must pass", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_built_in_scene_guids_are_allowed() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write(
            "Samples~/Demo/Demo.unity",
            f"--- !u!114\n  m_Script: {{guid: {'0' * 16}{'f' * 16}, type: 3}}\n",
        )
        sandbox.run()
        assert_exit_code(0, "Unity's built-in resource GUIDs live in no repository meta", sandbox.last_exit)
    finally:
        sandbox.destroy()


CASES = (
    case_valid_manual_passes,
    case_missing_page_fails,
    case_missing_anchor_fails,
    case_duplicate_heading_anchors_are_numbered,
    case_external_and_code_links_are_skipped,
    case_sample_scene_reference_must_resolve,
    case_built_in_scene_guids_are_allowed,
)


def main() -> int:
    assert_true(LINT.is_file(), f"lint not found at {LINT}")
    print("== documentation-link lint contract tests ==")
    failures = run_cases([(case.__name__, case) for case in CASES])
    print(f"== documentation-link lint: {failures} failure(s) ==")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
