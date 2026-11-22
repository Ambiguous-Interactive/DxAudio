"""Plan weekly releases and promote the exact candidate commit after validation."""

from __future__ import annotations

import argparse
import difflib
import json
import os
import re
from datetime import date, datetime
from pathlib import Path
from typing import TypedDict, cast
from urllib.parse import urlencode
from zoneinfo import ZoneInfo

from package_check import PACKAGE_DIRS, manifest_object
from release_service import api, registry, run
from sync_docs_version import DOC_PINS, sync_pins

STABLE = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")
UNRELEASED = re.compile(r"(?m)^## \[Unreleased\]$")
LEVELS = {
    "Fixed": 1,
    "Changed": 1,
    "Security": 1,
    "Added": 2,
    "Deprecated": 2,
    "Removed": 3,
    "Breaking": 3,
}
PRODUCT_PATHS = (*PACKAGE_DIRS, "Samples~", "Documentation~", "package.json", "LICENSE.md")
WORKFLOWS = ("ci.yml", "unity-tests.yml", "llm-lint.yml", "doc-links.yml")
REQUIRED_JOBS = {"ci.yml": "Build and package safety", "unity-tests.yml": "Unity CI Success"}
VERIFY_PREFIX = "release-verify/"


class RefTarget(TypedDict):
    sha: str


class Ref(TypedDict):
    object: RefTarget


class Repository(TypedDict):
    full_name: str


class WorkflowRun(TypedDict):
    id: int
    head_sha: str
    head_branch: str
    head_repository: Repository | None
    html_url: str
    event: str
    status: str
    conclusion: str | None


class RunList(TypedDict):
    workflow_runs: list[WorkflowRun]


class WorkflowJob(TypedDict):
    name: str
    head_sha: str
    conclusion: str | None
    html_url: str


class JobList(TypedDict):
    jobs: list[WorkflowJob]


class PageLink(TypedDict):
    html_url: str


class ReleaseState(TypedDict):
    draft: bool


def split_changelog(text: str) -> tuple[str, str, str]:
    headings = list(UNRELEASED.finditer(text))
    if len(headings) != 1:
        raise ValueError("Expected one Unreleased heading")
    heading = headings[0]
    following = re.search(r"(?m)^## ", text[heading.end() :])
    end = heading.end() + following.start() if following else len(text)
    return text[: heading.end()], text[heading.end() : end], text[end:]


def classify(version: str, notes: str) -> tuple[str | None, str]:
    if not STABLE.fullmatch(version):
        raise ValueError("Weekly releases require a stable package version")
    major, minor, patch = map(int, version.split("."))
    changed: dict[str, int] = {}
    section = ""
    for line in re.sub(r"<!--.*?-->", "", notes, flags=re.S).splitlines():
        if line.startswith("### "):
            section = line[4:].strip()
        elif re.match(r"^[-*+]\s+\S", line):
            if section not in LEVELS:
                raise ValueError(f"Unknown changelog section with entries: {section or '(none)'}")
            breaking = re.match(r"^[-*+]\s+\*\*Breaking:\*\*", line)
            changed["Breaking" if breaking else section] = 3 if breaking else LEVELS[section]
        elif line.strip() and not line.startswith((" ", "\t")):
            raise ValueError(f"Unexpected Unreleased content: {line}")
    level = max(changed.values(), default=0)
    if level == 0:
        return None, "No release notes"
    reasons = ", ".join(sorted(changed))
    if level == 3 and major:
        return f"{major + 1}.0.0", f"Major bump from {reasons}"
    if level >= 2:
        return f"{major}.{minor + 1}.0", f"Minor bump from {reasons}"
    return f"{major}.{minor}.{patch + 1}", f"Patch bump from {reasons}"


def rotate(text: str, version: str, day: date) -> str:
    prefix, notes, history = split_changelog(text)
    return (
        f"{prefix}\n\n## [{version}] - {day.isoformat()}\n\n{notes.strip()}\n\n{history}".rstrip()
        + "\n"
    )


def git(*args: str) -> str:
    return run("git", *args)


def summary(message: str) -> None:
    print(message)
    if target := os.getenv("GITHUB_STEP_SUMMARY"):
        with Path(target).open("a", encoding="utf-8") as output:
            output.write(message + "\n\n")


def read_version(ref: str = "HEAD") -> str:
    return cast(
        "str",
        json.loads(git("show", f"{ref}:package.json"), object_pairs_hook=manifest_object)[
            "version"
        ],
    )


def ref_sha(ref: str) -> str | None:
    data = cast("Ref | None", api(f"git/ref/{ref}", missing=True))
    return data["object"]["sha"] if data else None


def dispatch(workflow: str, ref: str, inputs: dict[str, str] | None = None) -> None:
    body: dict[str, object] = {"ref": ref}
    if inputs is not None:
        body["inputs"] = inputs
    result = cast(
        "PageLink | None", api(f"actions/workflows/{workflow}/dispatches", method="POST", body=body)
    )
    link = f" {result['html_url']}" if result else ""
    summary(f"Dispatched {workflow} on {ref}.{link}")


def validation_runs(branch: str, sha: str) -> dict[str, WorkflowRun | None]:
    params = urlencode(
        {"branch": branch, "event": "workflow_dispatch", "head_sha": sha, "per_page": 1}
    )
    return {
        workflow: next(
            iter(
                cast("RunList", api(f"actions/workflows/{workflow}/runs?{params}"))["workflow_runs"]
            ),
            None,
        )
        for workflow in WORKFLOWS
    }


def required_statuses(runs: dict[str, WorkflowRun | None], sha: str) -> dict[str, str]:
    verified: dict[str, str] = {}
    for workflow, context in REQUIRED_JOBS.items():
        check = runs[workflow]
        if check is None:
            raise ValueError(f"{workflow} has no validation run")
        pages = cast(
            "list[JobList]",
            api(f"actions/runs/{check['id']}/jobs?filter=latest&per_page=100", paginate=True),
        )
        jobs = [job for page in pages for job in page["jobs"] if job["name"] == context]
        if len(jobs) != 1 or jobs[0]["head_sha"] != sha or jobs[0]["conclusion"] != "success":
            raise ValueError(f"{workflow} has no successful {context} job on {sha}")
        verified[context] = jobs[0]["html_url"]
    return verified


def candidate_branch(version: str, base: str) -> str:
    return f"{VERIFY_PREFIX}v{version}-{base}"


def promote(branch: str, sha: str, *, retry: bool, dispatch_missing: bool = False) -> None:
    current = ref_sha(f"heads/{branch}")
    if current is None and not retry:
        return
    if current != sha:
        raise ValueError("Validation branch does not match the release commit")
    ancestry = git("rev-list", "--parents", "-n", "1", sha).split()
    if len(ancestry) != 2:
        raise ValueError("A release candidate must have one parent")
    base = ancestry[1]
    version = read_version(sha)
    if branch != candidate_branch(version, base):
        raise ValueError("Validation branch does not identify this release")
    main = ref_sha("heads/main")
    if main is None:
        raise ValueError("main is missing")
    if main != base and git("merge-base", main, sha) != sha:
        summary("main advanced while release checks ran. Prepare a new candidate from main.")
        return
    verify_edits(base, sha, version)
    tag = f"v{version}"
    runs = validation_runs(branch, sha)
    ready = True
    for workflow, check in runs.items():
        if check is None:
            if retry or dispatch_missing:
                dispatch(workflow, branch)
            ready = False
            continue
        if (
            check["head_sha"] != sha
            or check["head_branch"] != branch
            or check["event"] != "workflow_dispatch"
            or check["head_repository"] is None
            or check["head_repository"]["full_name"] != os.environ["GITHUB_REPOSITORY"]
        ):
            raise ValueError(f"Unexpected run identity for {workflow}")
        summary(f"{workflow}: {check['conclusion'] or check['status']} — {check['html_url']}")
        if check["status"] != "completed" or check["conclusion"] != "success":
            ready = False
            if retry and check["status"] == "completed":
                api(f"actions/runs/{check['id']}/rerun", method="POST")
    if not ready:
        return
    tag_sha = ref_sha(f"tags/{tag}")
    if tag_sha is not None and tag_sha != sha and git("rev-parse", f"{tag}^{{commit}}") != sha:
        raise ValueError(f"{tag} points to a different commit")
    statuses = required_statuses(runs, sha) if main == base else {}
    if os.getenv("AUTO_RELEASE_VALIDATE_ONLY") == "true":
        summary(
            f"All validation passed for {tag} at {sha}. Publication is disabled for this rehearsal."
        )
        return
    if main == base:
        # Rulesets exclude job checks from workflow_dispatch. Report the verified
        # results as commit statuses from the same GitHub Actions integration.
        for context, url in statuses.items():
            api(
                f"statuses/{sha}",
                method="POST",
                body={"context": context, "state": "success", "target_url": url},
            )
        api("git/refs/heads/main", method="PATCH", body={"sha": sha, "force": False})
        summary(f"Advanced main to the validated release commit {sha}.")
    if tag_sha is None:
        api("git/refs", method="POST", body={"ref": f"refs/tags/{tag}", "sha": sha})
    params = urlencode({"event": "workflow_dispatch", "head_sha": sha, "per_page": 100})
    releases = cast("RunList", api(f"actions/workflows/release.yml/runs?{params}"))["workflow_runs"]
    latest = next((release for release in releases if release["head_branch"] == tag), None)
    if latest is None:
        dispatch("release.yml", tag, {"expected_sha": sha})
    elif retry and latest["status"] == "completed" and latest["conclusion"] != "success":
        api(f"actions/runs/{latest['id']}/rerun", method="POST")
        summary(f"Retried publication for {tag}. {latest['html_url']}")


def plan(base: str, version: str, day: date) -> tuple[str, str, str] | None:
    changelog = git("show", f"{base}:CHANGELOG.md") + "\n"
    _, notes, _ = split_changelog(changelog)
    candidate, reason = classify(version, notes)
    if candidate is None:
        changed = git("diff", "--name-only", f"v{version}^{{commit}}", base, "--", *PRODUCT_PATHS)
        if changed:
            raise ValueError(f"Package changes need release notes:\n{changed}")
        summary(reason)
        return None
    if not re.search(rf"(?m)^## \[{re.escape(version)}\] - ", changelog):
        raise ValueError("Current package version has no dated changelog section")
    return (
        candidate,
        reason,
        rotate(changelog, candidate, day),
    )


def release_edits(base: str, version: str, candidate: str, notes: str) -> dict[str, str]:
    manifest = git("show", f"{base}:package.json") + "\n"
    next_manifest, updated = re.subn(
        r'(?m)^(\s*"version":\s*)"[^"]+"', rf'\g<1>"{candidate}"', manifest, count=1
    )
    context = git("show", f"{base}:.llm/context.md") + "\n"
    next_context, synced = re.subn(
        rf"(?m)^(- \*\*Version\*\*: ){re.escape(version)}$", rf"\g<1>{candidate}", context
    )
    if updated != 1 or synced != 1:
        raise ValueError("Package and context versions disagree")
    edits = {"package.json": next_manifest, "CHANGELOG.md": notes, ".llm/context.md": next_context}
    for path in DOC_PINS:
        text, problems = sync_pins(path, git("show", f"{base}:{path}") + "\n", version)
        if problems:
            raise ValueError("\n".join(problems))
        edits[path] = sync_pins(path, text, candidate)[0]
    return edits


def verify_edits(base: str, sha: str, version: str) -> None:
    previous = read_version(base)
    notes = git("show", f"{sha}:CHANGELOG.md")
    heading = re.search(rf"(?m)^## \[{re.escape(version)}\] - (\d{{4}}-\d{{2}}-\d{{2}})$", notes)
    if heading is None:
        raise ValueError("Candidate misses its dated notes")
    expected = plan(base, previous, date.fromisoformat(heading[1]))
    if expected is None or expected[0] != version:
        raise ValueError("Candidate version disagrees with the release notes")
    edits = release_edits(base, previous, version, expected[2])
    if set(git("diff", "--name-only", base, sha).splitlines()) != edits.keys():
        raise ValueError("Candidate changes files outside the release metadata")
    for path, content in edits.items():
        if git("show", f"{sha}:{path}") != content.strip():
            raise ValueError(f"Candidate contains unexpected changes in {path}")


def prepare(
    base: str, version: str, *, preview: bool, create: bool = True, retry: bool = True
) -> None:
    changes = plan(base, version, datetime.now(ZoneInfo("America/Los_Angeles")).date())
    if changes is None:
        return
    candidate, reason, notes = changes
    summary(f"{base}: {reason}. Proposed version: {candidate}.")
    edits = release_edits(base, version, candidate, notes)
    if preview:
        for path, text in edits.items():
            original = git("show", f"{base}:{path}") + "\n"
            print(
                "".join(
                    difflib.unified_diff(
                        original.splitlines(True), text.splitlines(True), f"a/{path}", f"b/{path}"
                    )
                )
            )
        return
    if ref_sha(f"tags/v{candidate}") or registry(candidate):
        raise ValueError(f"Version {candidate} already exists")
    branch = candidate_branch(candidate, base)
    head = ref_sha(f"heads/{branch}")
    if head is None:
        if not create:
            summary("No release candidate is pending for the current main commit.")
            return
        for path, text in edits.items():
            Path(path).write_text(text, encoding="utf-8")
        git(
            "-c",
            "user.name=github-actions[bot]",
            "-c",
            "user.email=41898282+github-actions[bot]@users.noreply.github.com",
            "commit",
            "-m",
            f"Bump version from {version} to {candidate}",
            "--",
            *edits,
        )
        git(
            "-c",
            "credential.helper=",
            "-c",
            "credential.helper=!gh auth git-credential",
            "push",
            "origin",
            f"HEAD:refs/heads/{branch}",
        )
        head = git("rev-parse", "HEAD")
    promote(branch, head, retry=retry, dispatch_missing=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "operation", choices=("preview", "prepare", "resume", "reconcile", "completed")
    )
    args = parser.parse_args()
    base = git("rev-parse", "origin/main")
    version = read_version(base)
    if args.operation == "preview":
        prepare(base, version, preview=True)
        return
    if args.operation == "completed":
        promote(os.environ["VALIDATION_BRANCH"], os.environ["VALIDATION_SHA"], retry=False)
        return

    retry = args.operation != "reconcile"
    current_tag = ref_sha(f"tags/v{version}")
    published = cast("ReleaseState | None", api(f"releases/tags/v{version}", missing=True))
    release_commit = git(
        "log", "-1", "--first-parent", "--format=%H", "-G", '"version":', base, "--", "package.json"
    )
    previous_branch = candidate_branch(version, git("rev-parse", f"{release_commit}^"))
    previous = ref_sha(f"heads/{previous_branch}")
    if not current_tag or not published or published["draft"] or registry(version) is None:
        if previous != release_commit:
            raise ValueError(
                f"Resolve pending version {version} through the manual release workflow"
            )
        promote(previous_branch, release_commit, retry=retry, dispatch_missing=True)
        return
    if previous == release_commit:
        api(f"git/refs/heads/{previous_branch}", method="DELETE")
    latest = registry("latest")
    if (
        latest is None
        or latest["version"] != version
        or git("merge-base", "--is-ancestor", f"v{version}^{{commit}}", base) != ""
    ):
        raise ValueError("Current version, main ancestry and npm latest must agree")
    prepare(base, version, preview=False, create=args.operation == "prepare", retry=retry)


if __name__ == "__main__":
    main()
