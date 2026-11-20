"""Negative controls for the DxAudio consumer dependency and payload policy."""

import io
import json
import sys
import tarfile
import tempfile
from pathlib import Path
import unittest

sys.dont_write_bytecode = True

from package_check import archive_paths, changelog_problems, dependency_problems, manifest_object, payload_problems, tooling_problems, version_problems


class PackagePolicyTests(unittest.TestCase):
    def test_dependency_map_is_empty(self):
        self.assertEqual([], dependency_problems({"dependencies": {}}))
        self.assertTrue(dependency_problems({}))
        self.assertTrue(dependency_problems({"dependencies": {"com.wallstop-studios.dxmessaging": "3.3.0"}}))
        for key in ("optionalDependencies", "peerDependencies", "bundleDependencies", "bundledDependencies", "scripts"):
            with self.subTest(key=key):
                self.assertTrue(dependency_problems({"dependencies": {}, key: {"prepare": "task"}}))

    def test_release_inputs_are_literal_and_unambiguous(self):
        self.assertEqual([], version_problems("1.2.3-beta.4", "v1.2.3-beta.4"))
        self.assertTrue(version_problems("1.2.3", "v1.2.4"))
        self.assertTrue(version_problems("1.2.3\nextra=1", None))
        self.assertTrue(version_problems(3, None))
        for version in ("01.2.3", "1.2.3-.", "1.2.3-01", "1.2.3+build"):
            self.assertTrue(version_problems(version, None))
        with self.assertRaises(ValueError):
            json.loads('{"version":"1.2.3","version":"1.2.4"}', object_pairs_hook=manifest_object)

    def test_changelog_has_a_rendered_url_and_a_current_heading(self):
        changelog = "# Changelog\n\n## [Unreleased]\n"
        rendered = "https://github.com/owner/repo/blob/main/CHANGELOG.md"
        self.assertEqual([], changelog_problems(rendered, "1.2.3", changelog))
        self.assertTrue(changelog_problems("https://raw.githubusercontent.com/owner/repo/CHANGELOG.md", "1.2.3", changelog))
        for invalid in ("https://github.com/owner/repo/raw/main/CHANGELOG.md", "https://example.com/", "https://["):
            self.assertTrue(changelog_problems(invalid, "1.2.3", changelog))
        self.assertTrue(changelog_problems(rendered, "1.2.3", "# Changelog\n\n## [1.0.0]\n"))

    def test_a_required_assembly_cannot_reference_an_optional_assembly(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Runtime"
            runtime.mkdir()
            required = {"name": "Required", "overrideReferences": True, "references": ["Optional"]}
            optional = {
                "name": "Optional",
                "overrideReferences": True,
                "references": ["WallstopStudios.DxMessaging"],
                "defineConstraints": ["DXMESSAGING_PRESENT"],
                "versionDefines": [{
                    "name": "com.wallstop-studios.dxmessaging",
                    "define": "DXMESSAGING_PRESENT",
                    "expression": "",
                }],
            }
            (runtime / "optional.asmdef").write_text(json.dumps(optional), encoding="utf-8")
            required_path = runtime / "required.asmdef"
            required_path.write_text(json.dumps(required), encoding="utf-8")
            self.assertTrue(dependency_problems({"dependencies": {}}, root))
            required["references"].clear()
            required_path.write_text(json.dumps(required), encoding="utf-8")
            self.assertEqual([], dependency_problems({"dependencies": {}}, root))
            (runtime / "hidden.asmref").write_text('{"reference":"Outside"}', encoding="utf-8")
            self.assertTrue(dependency_problems({"dependencies": {}}, root))
            (runtime / "hidden.asmref").unlink()
            scratch = runtime / "scratch~"
            scratch.mkdir()
            (scratch / "CanBeInvalid.asmdef").write_text('{"name":"Scratch"}', encoding="utf-8")
            self.assertEqual([], dependency_problems({"dependencies": {}}, root))
            required["references"] = ["Optional"]
            optional["defineConstraints"].append("UNITY_INCLUDE_TESTS")
            required_path.write_text(json.dumps(required), encoding="utf-8")
            (runtime / "optional.asmdef").write_text(json.dumps(optional), encoding="utf-8")
            errors = dependency_problems({"dependencies": {}}, root)
            self.assertTrue(any("test-only assembly Optional" in error for error in errors))

    def test_archive_contains_every_imported_assembly(self):
        self.assertTrue(any("Runtime/WallstopStudios.DxAudio.asmdef" in error for error in payload_problems(set())))

    def test_shipping_tooling_fails_with_or_without_metadata(self):
        for name in ("Analyzer.dll", "Analyzer.dll.meta", "csc.rsp", "rules.ruleset.meta", ".globalconfig"):
            with self.subTest(name=name):
                path = f"Runtime/{name}"
                self.assertTrue(tooling_problems({path}))
                self.assertTrue(any(path in message for message in payload_problems({path})))

    def test_archive_rejects_traversal_links_and_case_collisions(self):
        def check(entries):
            with tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "fixture.tgz"
                with tarfile.open(path, "w:gz") as archive:
                    for name, kind in entries:
                        entry = tarfile.TarInfo(name)
                        entry.type = kind
                        archive.addfile(entry, io.BytesIO(b"") if entry.isfile() else None)
                return archive_paths(path)

        good = [("package/Runtime/a.cs", tarfile.REGTYPE)]
        self.assertEqual(({"Runtime/a.cs"}, []), check(good))
        for entry in (
            ("package/../outside", tarfile.REGTYPE),
            ("package/link", tarfile.SYMTYPE),
            ("package/Runtime/A.cs", tarfile.REGTYPE),
            ("package/Runtime/a.cs/nested", tarfile.REGTYPE),
            ("package/Runtime/CON.cs", tarfile.REGTYPE),
            ("package/Runtime/name. ", tarfile.REGTYPE),
        ):
            with self.subTest(entry=entry):
                self.assertTrue(check(good + [entry])[1])

    def test_git_ignores_hidden_development_trees(self):
        self.assertEqual([], tooling_problems({".net-tests/Analyzer.dll"}, unity_visible_only=True))
        self.assertTrue(tooling_problems({".net-tests/Analyzer.dll"}))


if __name__ == "__main__":
    unittest.main()
