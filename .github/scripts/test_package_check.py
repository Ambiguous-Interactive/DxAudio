"""Negative controls for the DxAudio consumer dependency and payload policy."""

import io
import json
import sys
import tarfile
import tempfile
from pathlib import Path
import unittest

sys.dont_write_bytecode = True

from package_check import archive_paths, dependency_problems, payload_problems, tooling_problems


class PackagePolicyTests(unittest.TestCase):
    def test_dependency_map_is_empty(self):
        self.assertEqual([], dependency_problems({"dependencies": {}}))
        self.assertTrue(dependency_problems({}))
        self.assertTrue(dependency_problems({"dependencies": {"com.wallstop-studios.dxmessaging": "3.3.0"}}))

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
        ):
            with self.subTest(entry=entry):
                self.assertTrue(check(good + [entry])[1])

    def test_git_ignores_hidden_development_trees(self):
        self.assertEqual([], tooling_problems({".net-tests/Analyzer.dll"}, unity_visible_only=True))
        self.assertTrue(tooling_problems({".net-tests/Analyzer.dll"}))


if __name__ == "__main__":
    unittest.main()
