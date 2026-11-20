"""Apply the pinned upstream NUnit and Unity fixture policies to DxAudio."""

from __future__ import annotations

import importlib
import re
import sys
from pathlib import Path

PORTABLE_TEST_PATHS = ("scripts/lint_tests.py",)
MADE_OBJECTS = "Tests/Editor/Unity/MadeObjects.cs"
SCOPED_OWNER = r"\busing\s*\(?\s*MadeObjects\s+\w+\s*="
TEARDOWN = r"\[\s*(?:NUnit\.Framework\.)?TearDown(?:Attribute)?\s*\]"
CLEANUP = re.compile(r"\bfinally\s*\{|(?:" + TEARDOWN + r")\s*[^;{}]*\{")


def native_cleanup(checker: object, text: str, line: int) -> bool:
    """Recognize explicit cleanup of early-failure paths and fixture scopes."""
    source = checker.Source(text)
    for block in CLEANUP.finditer(source.code):
        start = block.end() - 1
        end = checker.closing(source.code, start, "{", "}")
        if end >= 0 and source.line_at(start) <= line <= source.line_at(end):
            return True
    return False


def analyze(checker: object, text: str, relative: str, *, editor_only: bool) -> list:
    # The optional integration test assembly has a local tracking helper,
    # rather than a dependency on the main NUnit assembly's cleanup scope.
    integration_owner = (
        relative.startswith("Tests/Editor/Integrations/")
        and re.search(TEARDOWN, text)
        and re.search(r"\b_made\.Add\(", text)
    )
    if integration_owner:
        normalized = re.sub(r"\bMade(?=\s*[<(])", "Track", text)
    else:
        normalized = text
    owned_scope = bool(re.search(SCOPED_OWNER, checker.Source(normalized).code)) or integration_owner
    problems = []
    for finding in checker.analyze(normalized, relative, editor_only=editor_only):
        if finding.code == "UNH001" and (relative == MADE_OBJECTS or native_cleanup(checker, normalized, finding.line)):
            continue
        if finding.code == "UNH003" and owned_scope:
            continue
        problems.append(finding)
    return problems


def run_test_checks(root: Path, modules: Path) -> list[str]:
    if str(modules) not in sys.path:
        sys.path.insert(0, str(modules))
    sys.dont_write_bytecode = True
    checker = importlib.import_module("lint_tests")
    checker.HELPERS = (MADE_OBJECTS,)
    if not (root / MADE_OBJECTS).is_file():
        raise ValueError("the DxAudio Unity ownership helper is missing")
    paths = checker.selected_sources(root, None)
    scope = checker.AssemblyScope(root)
    problems = []
    for path in paths:
        relative = path.relative_to(root).as_posix()
        findings = analyze(checker, path.read_text(encoding="utf-8-sig"), relative, editor_only=scope.editor_only(path))
        for finding in findings:
            message = f"{relative}:{finding.line} {finding.code} {finding.message}"
            if finding.advisory:
                print(message)
            else:
                problems.append(message)
    print(f"[unity-helpers-tests] checked {len(paths)} Unity test sources")

    invalid = """using UnityEditor;
namespace WallstopStudios.Control
{
    public class FixturePerformanceTests
    {
        [Test]
        public void Bad_Name()
        {
            GameObject leaked = new GameObject("untracked");
            Object.DestroyImmediate(leaked);
            Assert.IsNull(leaked);
            for (int i = 0; i < 50_000; ++i) { }
            AssetDatabase.Refresh();
            Thread.Sleep(10);
        }
        public IEnumerable<TestCaseData> Cases()
        {
            yield return new TestCaseData(1);
        }
        [UnityTest]
        [TestCaseSource(nameof(Cases))]
        public IEnumerator CoroutineCase(int value)
        {
            yield return new WaitForEndOfFrame();
        }
    }
}
"""
    codes = {finding.code for finding in analyze(checker, invalid, "Tests/Runtime/Control.cs", editor_only=False)}
    expected = {f"UNH{number:03d}" for number in range(1, 13)}
    if not expected.issubset(codes):
        problems.append(f"Test policy missed the following negative controls: {sorted(expected - codes)}")
    valid = """namespace WallstopStudios.Control
{
    public class Fixture
    {
        [Test]
        public void NamedOwnedTest()
        {
            using MadeObjects made = new();
            GameObject owned = made.Track(new GameObject("tracked"));
            made.DestroyNow(owned);
        }
        [TearDown]
        public void Clear() { Object.DestroyImmediate(alreadyTracked); }
    }
}
"""
    if analyze(checker, valid, "Tests/Editor/Control.cs", editor_only=True):
        problems.append("Test policy rejected scoped and teardown-based cleanup")
    return problems
