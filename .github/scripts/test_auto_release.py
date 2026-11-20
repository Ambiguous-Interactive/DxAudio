"""Regression tests for version decisions and release artifacts."""

# Keep these tests runnable with the standard library on release runners.
# ruff: noqa: PT027

import io
import tarfile
import tempfile
import unittest
from datetime import date
from pathlib import Path
from unittest.mock import patch

from auto_release import WORKFLOWS, WorkflowRun, classify, rotate, tag_release
from release_assets import build_unitypackage, npm_integrity, publish_npm, read_member


class ReleaseDecisionTests(unittest.TestCase):
    def test_release_policy(self) -> None:
        for current, notes, expected in (
            ("0.3.0", "", None),
            ("0.3.0", "### Fixed\n\n- Fix a bug.\n", "0.3.1"),
            ("0.3.0", "### Fixed\n\n- Fix a bug.\n\n### Added\n\n- Add a feature.\n", "0.4.0"),
            ("0.3.0", "### Changed\n\n- **Breaking:** Rename an API.\n", "0.4.0"),
            ("1.7.2", "### Removed\n\n- Remove an API.\n", "2.0.0"),
            ("1.7.2", "### Deprecated\n\n- Deprecate an API.\n", "1.8.0"),
        ):
            with self.subTest(current=current, notes=notes):
                assert expected == classify(current, notes)[0]

    def test_unknown_sections_and_prerelease_baselines_fail(self) -> None:
        for current, notes in (
            ("1.0.0", "### Misc\n- Change something."),
            ("1.0.0-rc.1", "### Fixed\n- Fix a bug."),
        ):
            with self.subTest(current=current, notes=notes), self.assertRaises(ValueError):
                classify(current, notes)

    def test_rotate_keeps_notes_and_history(self) -> None:
        source = "# Changelog\n\n## [Unreleased]\n\n### Added\n\n- Add a feature.\n\n## [0.3.0] - 2026-11-15\n"
        result = rotate(source, "0.4.0", date(2026, 11, 22))
        assert "## [Unreleased]\n\n## [0.4.0] - 2026-11-22\n\n### Added" in result
        assert result.endswith("## [0.3.0] - 2026-11-15\n")


class PromotionTests(unittest.TestCase):
    repository = "Ambiguous-Interactive/DxAudio"
    sha = "a" * 40
    branch = "release-verify/v0.4.0"

    def run_data(self, conclusion: str | None) -> WorkflowRun:
        return {
            "id": 17,
            "status": "completed",
            "conclusion": conclusion,
            "event": "workflow_dispatch",
            "head_sha": self.sha,
            "head_branch": self.branch,
            "head_repository": {"full_name": self.repository},
            "html_url": "https://example.com/run",
        }

    def test_cannot_tag_until_every_check_passes(self) -> None:
        for conclusion in (None, "failure", "cancelled", "skipped", "success"):
            with self.subTest(conclusion=conclusion):
                checks: dict[str, WorkflowRun | None] = dict.fromkeys(
                    WORKFLOWS, self.run_data("success")
                )
                checks["unity-tests.yml"] = self.run_data(conclusion) if conclusion else None
                with (
                    patch.dict(
                        "os.environ",
                        {"GITHUB_REPOSITORY": self.repository, "AUTO_RELEASE_VALIDATE_ONLY": ""},
                    ),
                    patch("auto_release.find_release_pr", return_value={"number": 3}),
                    patch("auto_release.read_version", return_value="0.4.0"),
                    patch("auto_release.ref_sha", side_effect=[self.sha, None]),
                    patch("auto_release.validation_runs", return_value=checks),
                    patch("auto_release.api", return_value={"workflow_runs": []}) as api,
                    patch("auto_release.dispatch") as dispatch,
                    patch("auto_release.summary"),
                ):
                    tag_release("0.4.0", self.sha, "release-app", retry=False)
                if conclusion == "success":
                    api.assert_any_call(
                        "git/refs", method="POST", body={"ref": "refs/tags/v0.4.0", "sha": self.sha}
                    )
                    dispatch.assert_called_once_with(
                        "release.yml", "v0.4.0", {"expected_sha": self.sha}
                    )
                else:
                    api.assert_not_called()
                    dispatch.assert_not_called()

    def test_retry_preserves_existing_tag_and_does_not_dispatch_twice(self) -> None:
        checks: dict[str, WorkflowRun | None] = dict.fromkeys(WORKFLOWS, self.run_data("success"))
        with (
            patch.dict(
                "os.environ",
                {"GITHUB_REPOSITORY": self.repository, "AUTO_RELEASE_VALIDATE_ONLY": ""},
            ),
            patch("auto_release.find_release_pr", return_value={"number": 3}),
            patch("auto_release.read_version", return_value="0.4.0"),
            patch("auto_release.ref_sha", return_value=self.sha),
            patch("auto_release.git", return_value=self.sha),
            patch("auto_release.validation_runs", return_value=checks),
            patch(
                "auto_release.api",
                return_value={
                    "workflow_runs": [{"head_branch": "v0.4.0", "status": "in_progress"}]
                },
            ) as api,
            patch("auto_release.dispatch") as dispatch,
            patch("auto_release.summary"),
        ):
            tag_release("0.4.0", self.sha, "release-app", retry=True)
            api.assert_called_once()
            dispatch.assert_not_called()


class ArchiveTests(unittest.TestCase):
    def test_existing_npm_version_requires_matching_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as scratch:
            archive = Path(scratch) / "archive.tgz"
            archive.write_bytes(b"validated archive")
            for digest in ("unexpected digest", npm_integrity(archive)):
                with (
                    self.subTest(digest=digest),
                    patch("release_assets.registry", return_value={"dist": {"integrity": digest}}),
                    patch("release_assets.run") as publish,
                ):
                    if digest == npm_integrity(archive):
                        publish_npm("0.4.0", archive, False)
                    else:
                        with self.assertRaisesRegex(ValueError, "npm integrity mismatch"):
                            publish_npm("0.4.0", archive, False)
                    publish.assert_not_called()

    def test_unity_archive_is_deterministic_and_preserves_guid(self) -> None:
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            source = root / "package.tgz"
            guid = "a" * 32
            with tarfile.open(source, "w:gz") as archive:
                entries = {
                    "package/package.json": b"{}",
                    "package/package.json.meta": f"fileFormatVersion: 2\nguid: {guid}\n".encode(),
                    "package/Samples~/demo.txt": b"sample",
                }
                for name, data in entries.items():
                    member = tarfile.TarInfo(name)
                    member.size = len(data)
                    archive.addfile(member, io.BytesIO(data))
            first = root / "first.unitypackage"
            second = root / "second.unitypackage"
            build_unitypackage(source, first)
            build_unitypackage(source, second)
            assert first.read_bytes() == second.read_bytes()
            assert npm_integrity(source).startswith("sha512-")
            with tarfile.open(first) as archive:
                assert [
                    f"{guid}/asset",
                    f"{guid}/asset.meta",
                    f"{guid}/pathname",
                ] == archive.getnames()
                assert (
                    read_member(archive, f"{guid}/pathname")
                    == b"Packages/com.wallstop-studios.dxaudio/package.json"
                )


if __name__ == "__main__":
    unittest.main()
