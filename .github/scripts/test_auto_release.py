"""Regression tests for version decisions and release artifacts."""

# Keep these tests runnable with the standard library on release runners.
# ruff: noqa: PT027

import io
import json
import tarfile
import tempfile
import unittest
from datetime import date
from pathlib import Path
from unittest.mock import patch

from auto_release import (
    WORKFLOWS,
    WorkflowRun,
    classify,
    dispatch,
    prepare,
    promote,
    release_edits,
    required_statuses,
    rotate,
)
from package_check import ROOT
from release_assets import (
    build_unitypackage,
    github_release,
    npm_integrity,
    publish_npm,
    read_member,
)


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

    def test_release_edits_bump_every_public_pin(self) -> None:
        pins = {
            "README.md": 2,
            "Documentation~/install.md": 2,
            "docs/images/brand/dxaudio-banner.svg": 1,
            "docs/images/brand/dxaudio-banner-light.svg": 1,
        }
        paths = ("package.json", ".llm/context.md", *pins)
        base = {path: (ROOT / path).read_text(encoding="utf-8") for path in paths}
        version = json.loads(base["package.json"])["version"]
        candidate = "99.1.2"

        def show(command: str, ref: str) -> str:
            assert command == "show"
            return base[ref.removeprefix("base:")].strip()

        with patch("auto_release.git", side_effect=show):
            edits = release_edits("base", version, candidate, "new notes\n")

        for path, count in pins.items():
            with self.subTest(path=path):
                assert edits[path].count(candidate) == count
                assert edits[path].replace(candidate, version) == base[path]


class PromotionTests(unittest.TestCase):
    repository = "Ambiguous-Interactive/DxAudio"
    sha = "a" * 40
    base = "b" * 40
    branch = f"release-verify/v0.4.0-{base}"

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

    def api_response(self, path: str, **_kwargs: object) -> object:
        if path.startswith("actions/runs/"):
            return [
                {
                    "jobs": [
                        {
                            "name": context,
                            "head_sha": self.sha,
                            "conclusion": "success",
                            "html_url": "https://example.com/gate",
                        }
                        for context in ("Build and package safety", "Unity CI Success")
                    ]
                }
            ]
        return {"workflow_runs": []}

    def test_dispatch_accepts_a_response_without_a_run_url(self) -> None:
        with (
            patch("auto_release.api", return_value=None) as api,
            patch("auto_release.summary") as summary,
        ):
            dispatch("ci.yml", self.branch)
        api.assert_called_once_with(
            "actions/workflows/ci.yml/dispatches", method="POST", body={"ref": self.branch}
        )
        summary.assert_called_once_with(f"Dispatched ci.yml on {self.branch}.")

    def test_reconciliation_uses_only_an_existing_candidate(self) -> None:
        for existing in (None, self.sha):
            with (
                self.subTest(candidate=existing),
                patch("auto_release.plan", return_value=("0.4.0", "Minor bump", "notes")),
                patch("auto_release.release_edits", return_value={}),
                patch("auto_release.ref_sha", side_effect=[None, existing]),
                patch("auto_release.registry", return_value=None),
                patch("auto_release.git") as git,
                patch("auto_release.summary"),
                patch("auto_release.promote") as promote,
            ):
                prepare(self.base, "0.3.0", preview=False, create=False, retry=False)
            git.assert_not_called()
            if existing:
                promote.assert_called_once_with(
                    self.branch, existing, retry=False, dispatch_missing=True
                )
            else:
                promote.assert_not_called()

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
                    patch("auto_release.verify_edits"),
                    patch("auto_release.read_version", return_value="0.4.0"),
                    patch("auto_release.ref_sha", side_effect=[self.sha, self.base, None]),
                    patch("auto_release.git", return_value=f"{self.sha} {self.base}"),
                    patch("auto_release.validation_runs", return_value=checks),
                    patch("auto_release.api", side_effect=self.api_response) as api,
                    patch("auto_release.dispatch") as dispatch,
                    patch("auto_release.summary"),
                ):
                    promote(self.branch, self.sha, retry=False)
                if conclusion == "success":
                    api.assert_any_call(
                        f"statuses/{self.sha}",
                        method="POST",
                        body={
                            "context": "Build and package safety",
                            "state": "success",
                            "target_url": "https://example.com/gate",
                        },
                    )
                    api.assert_any_call(
                        "git/refs/heads/main",
                        method="PATCH",
                        body={"sha": self.sha, "force": False},
                    )
                    api.assert_any_call(
                        "git/refs", method="POST", body={"ref": "refs/tags/v0.4.0", "sha": self.sha}
                    )
                    dispatch.assert_called_once_with(
                        "release.yml", "v0.4.0", {"expected_sha": self.sha}
                    )
                else:
                    api.assert_not_called()
                    dispatch.assert_not_called()

    def test_required_job_must_succeed_on_the_candidate_sha(self) -> None:
        runs: dict[str, WorkflowRun | None] = dict.fromkeys(WORKFLOWS, self.run_data("success"))
        for job_sha, conclusion in ((self.base, "success"), (self.sha, "skipped")):
            with self.subTest(sha=job_sha, conclusion=conclusion):
                job = {
                    "name": "Build and package safety",
                    "head_sha": job_sha,
                    "conclusion": conclusion,
                    "html_url": "https://example.com/gate",
                }
                with (
                    patch("auto_release.api", return_value=[{"jobs": [job]}]),
                    self.assertRaisesRegex(ValueError, "no successful Build and package safety"),
                ):
                    required_statuses(runs, self.sha)

    def test_conflicting_tag_blocks_the_main_update(self) -> None:
        checks = dict.fromkeys(WORKFLOWS, self.run_data("success"))
        with (
            patch.dict(
                "os.environ",
                {"GITHUB_REPOSITORY": self.repository, "AUTO_RELEASE_VALIDATE_ONLY": ""},
            ),
            patch("auto_release.verify_edits"),
            patch("auto_release.read_version", return_value="0.4.0"),
            patch("auto_release.ref_sha", side_effect=[self.sha, self.base, "wrong-tag"]),
            patch("auto_release.git", side_effect=[f"{self.sha} {self.base}", "wrong-tag"]),
            patch("auto_release.validation_runs", return_value=checks),
            patch("auto_release.api") as api,
            patch("auto_release.summary"),
            self.assertRaisesRegex(ValueError, "points to a different commit"),
        ):
            promote(self.branch, self.sha, retry=False)
        api.assert_not_called()

    def test_retry_preserves_existing_tag_and_does_not_dispatch_twice(self) -> None:
        checks: dict[str, WorkflowRun | None] = dict.fromkeys(WORKFLOWS, self.run_data("success"))
        with (
            patch.dict(
                "os.environ",
                {"GITHUB_REPOSITORY": self.repository, "AUTO_RELEASE_VALIDATE_ONLY": ""},
            ),
            patch("auto_release.verify_edits"),
            patch("auto_release.read_version", return_value="0.4.0"),
            patch("auto_release.ref_sha", return_value=self.sha),
            patch("auto_release.git", side_effect=[f"{self.sha} {self.base}", self.sha]),
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
            promote(self.branch, self.sha, retry=True)
            api.assert_called_once()
            dispatch.assert_not_called()


class ArchiveTests(unittest.TestCase):
    def test_missing_tag_prevents_draft_creation(self) -> None:
        archive = Path("example.tgz")
        with (
            patch("release_assets.api", side_effect=[[], None]) as api,
            self.assertRaisesRegex(ValueError, "Missing Git tag"),
        ):
            github_release("stage", "v0.4.0", archive, False)
        assert api.call_count == 2

    def test_new_draft_uploads_to_the_returned_release_id(self) -> None:
        with tempfile.TemporaryDirectory() as scratch:
            folder = Path(scratch)
            tarball = folder / "example.tgz"
            (folder / "notes.md").write_text("Release notes", encoding="utf-8")
            for suffix in (".tgz", ".tgz.sha256", ".unitypackage", ".unitypackage.sha256"):
                tarball.with_suffix(suffix).write_bytes(b"archive bytes")
            with (
                patch(
                    "release_assets.api",
                    side_effect=[
                        [],
                        {"object": {"sha": "a" * 40}},
                        {"id": 42, "tag_name": "v0.4.0", "draft": True},
                        [],
                    ],
                ) as api,
                patch("release_assets.run", return_value="a" * 40),
                patch("release_assets.upload_asset") as upload,
                patch("release_assets.verify_asset"),
            ):
                github_release("stage", "v0.4.0", tarball, False)
            assert [call.args[0] for call in upload.call_args_list] == [42, 42, 42, 42]
            api.assert_any_call("releases/42/assets?per_page=100", paginate=True)
            assert api.call_count == 4

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

    def test_new_npm_version_can_take_minutes_to_appear(self) -> None:
        with tempfile.TemporaryDirectory() as scratch:
            archive = Path(scratch) / "archive.tgz"
            archive.write_bytes(b"validated archive")
            published = {"version": "0.4.0", "dist": {"integrity": npm_integrity(archive)}}
            elapsed = 0.0
            visible_after = 0

            def registry(version: str) -> object:
                return published if version == "latest" or visible_after <= elapsed else None

            def monotonic() -> float:
                return elapsed

            def sleep(seconds: float) -> None:
                nonlocal elapsed
                elapsed += seconds

            for visible_after in (200, 301):
                elapsed = 0.0
                with (
                    self.subTest(visible_after=visible_after),
                    patch("release_assets.registry", side_effect=registry),
                    patch("release_assets.run") as publish,
                    patch("release_assets.time.monotonic", side_effect=monotonic),
                    patch("release_assets.time.sleep", side_effect=sleep),
                ):
                    if visible_after == 200:
                        publish_npm("0.4.0", archive, False)
                        assert elapsed == 200
                    else:
                        with self.assertRaisesRegex(ValueError, "resume this release"):
                            publish_npm("0.4.0", archive, False)
                        assert elapsed == 300
                    publish.assert_called_once_with(
                        "npm", "publish", str(archive), "--access", "public"
                    )

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
