"""Plan weekly releases and promote the exact merged commit after validation."""

from __future__ import annotations

import argparse
import difflib
import json
import os
import re
from datetime import datetime
from pathlib import Path
from typing import TYPE_CHECKING, TypedDict, cast
from urllib.parse import urlencode
from zoneinfo import ZoneInfo

from package_check import PACKAGE_DIRS, manifest_object
from release_service import api, registry, run

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
RELEASE_FILES = ("package.json", "CHANGELOG.md", ".llm/context.md")
PRODUCT_PATHS = (*PACKAGE_DIRS, "Samples~", "Documentation~", "package.json", "LICENSE.md")
WORKFLOWS = ("ci.yml", "unity-tests.yml", "llm-lint.yml", "doc-links.yml")
PR_PREFIX = "release/weekly-"
VERIFY_PREFIX = "release-verify/"


if TYPE_CHECKING:
    from datetime import date


class RefTarget(TypedDict):
    sha: str


class Ref(TypedDict):
    object: RefTarget


class Repository(TypedDict):
    full_name: str


class PullRef(TypedDict):
    ref: str
    repo: Repository | None


class Author(TypedDict):
    login: str


class Pull(TypedDict):
    number: int
    merge_commit_sha: str | None
    merged_at: str | None
    base: PullRef
    head: PullRef
    user: Author


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


def find_release_pr(sha: str, slug: str) -> Pull | None:
    pages = cast("list[list[Pull]]", api(f"commits/{sha}/pulls?per_page=100", paginate=True))
    for page in pages:
        for pr in page:
            head_repo = pr["head"]["repo"]
            if (
                pr["merge_commit_sha"] == sha
                and pr["merged_at"]
                and pr["base"]["ref"] == "main"
                and pr["head"]["ref"].startswith(PR_PREFIX)
                and head_repo
                and head_repo["full_name"] == os.environ["GITHUB_REPOSITORY"]
                and pr["user"]["login"] == f"{slug}[bot]"
            ):
                return pr
    return None


def prepared_commit(version: str, slug: str) -> str | None:
    for sha in git(
        "log",
        "--first-parent",
        "--format=%H",
        "-G",
        '"version":',
        "origin/main",
        "--",
        "package.json",
    ).splitlines():
        if read_version(sha) != version:
            continue
        pr = find_release_pr(sha, slug)
        if pr:
            return sha
        break
    return None


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


def tag_release(version: str, sha: str, slug: str, *, retry: bool) -> None:
    branch = f"{VERIFY_PREFIX}v{version}"
    if (
        not find_release_pr(sha, slug)
        or read_version(sha) != version
        or read_version("origin/main") != version
    ):
        raise ValueError("Validation source does not match the current merged release PR")
    tag = f"v{version}"
    current = ref_sha(f"heads/{branch}")
    if current is None and retry:
        api("git/refs", method="POST", body={"ref": f"refs/heads/{branch}", "sha": sha})
    elif current != sha:
        raise ValueError("Validation branch does not match the release commit")

    runs = validation_runs(branch, sha)
    ready = True
    for workflow, check in runs.items():
        if check is None:
            if retry:
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
    if os.getenv("AUTO_RELEASE_VALIDATE_ONLY") == "true":
        summary(
            f"All validation passed for {tag} at {sha}. Publication is disabled for this rehearsal."
        )
        return
    tag_sha = ref_sha(f"tags/{tag}")
    if tag_sha is None:
        api("git/refs", method="POST", body={"ref": f"refs/tags/{tag}", "sha": sha})
    elif git("rev-parse", f"{tag}^{{commit}}") != sha:
        raise ValueError(f"{tag} points to a different commit")
    params = urlencode({"event": "workflow_dispatch", "head_sha": sha, "per_page": 100})
    releases = cast("RunList", api(f"actions/workflows/release.yml/runs?{params}"))["workflow_runs"]
    latest = next((release for release in releases if release["head_branch"] == tag), None)
    if latest is None:
        dispatch("release.yml", tag, {"expected_sha": sha})
    elif retry and latest["status"] == "completed" and latest["conclusion"] != "success":
        api(f"actions/runs/{latest['id']}/rerun", method="POST")
        summary(f"Retried publication for {tag}. {latest['html_url']}")


def plan(base: str, version: str) -> tuple[str, str, str] | None:
    note_path = Path("CHANGELOG.md")
    changelog = note_path.read_text(encoding="utf-8")
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
        rotate(changelog, candidate, datetime.now(ZoneInfo("America/Los_Angeles")).date()),
    )


def prepare(base: str, version: str, *, preview: bool, slug: str) -> None:
    changes = plan(base, version)
    if changes is None:
        return
    candidate, reason, notes = changes
    summary(f"{base}: {reason}. Proposed version: {candidate}.")
    manifest = Path("package.json").read_text(encoding="utf-8")
    next_manifest, updated = re.subn(
        r'(?m)^(\s*"version":\s*)"[^"]+"', rf'\g<1>"{candidate}"', manifest, count=1
    )
    context = Path(".llm/context.md").read_text(encoding="utf-8")
    next_context, synced = re.subn(
        rf"(?m)^(- \*\*Version\*\*: ){re.escape(version)}$", rf"\g<1>{candidate}", context
    )
    if updated != 1 or synced != 1:
        raise ValueError("Package and context versions disagree")
    edits = dict(zip(RELEASE_FILES, (next_manifest, notes, next_context), strict=True))
    for path, text in edits.items():
        original = Path(path).read_text(encoding="utf-8")
        if preview:
            print(
                "".join(
                    difflib.unified_diff(
                        original.splitlines(True), text.splitlines(True), f"a/{path}", f"b/{path}"
                    )
                )
            )
        else:
            Path(path).write_text(text, encoding="utf-8")
    if preview:
        return
    if ref_sha(f"tags/v{candidate}") or registry(candidate):
        raise ValueError(f"Version {candidate} already exists")
    branch = f"{PR_PREFIX}{base}"
    prs = [
        pr
        for page in cast(
            "list[list[Pull]]", api("pulls?base=main&state=open&per_page=100", paginate=True)
        )
        for pr in page
        if pr["head"]["ref"].startswith(PR_PREFIX)
        and (repo := pr["head"]["repo"])
        and repo["full_name"] == os.environ["GITHUB_REPOSITORY"]
    ]
    owned = f"{slug}[bot]"
    if any(pr["user"]["login"] != owned for pr in prs):
        raise ValueError("A weekly release branch has a pull request owned by another actor")
    if ref_sha("heads/main") != base:
        raise ValueError("main advanced during preparation; run again")
    existing = next((pr for pr in prs if pr["head"]["ref"] == branch), None)
    head = ref_sha(f"heads/{branch}")
    if head:
        if (
            git("rev-parse", f"{head}^") != base
            or git("show", "-s", "--format=%ae", head) != f"{owned}@users.noreply.github.com"
            or set(git("diff", "--name-only", base, head).splitlines()) != set(RELEASE_FILES)
            or read_version(head) != candidate
        ):
            raise ValueError(f"{branch} does not match the generated release")
    else:
        git(
            "-c",
            f"user.name={owned}",
            "-c",
            f"user.email={owned}@users.noreply.github.com",
            "commit",
            "-m",
            f"Bump version from {version} to {candidate}",
            "--",
            *RELEASE_FILES,
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
    for pr in prs:
        if pr["head"]["ref"] != branch:
            api(f"pulls/{pr['number']}", method="PATCH", body={"state": "closed"})
    arm_merge(existing, branch, candidate, reason, head)


def arm_merge(existing: Pull | None, branch: str, version: str, reason: str, head: str) -> None:
    title = f"Release DxAudio {version}"
    body = f"DISCLOSURE: LLM-GENERATED TEXT\n\n**Why:** {reason}.\n\n**What:**\n- Update the package version and release notes."
    if existing:
        number = existing["number"]
        api(f"pulls/{number}", method="PATCH", body={"title": title, "body": body})
    else:
        number = cast(
            "Pull",
            api(
                "pulls",
                method="POST",
                body={"base": "main", "head": branch, "title": title, "body": body},
            ),
        )["number"]
    run("gh", "pr", "merge", str(number), "--auto", "--squash", "--match-head-commit", head)
    summary(f"Release PR #{number} for {version} is ready for required checks.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "operation", choices=("preview", "prepare", "resume", "completed", "merged")
    )
    args = parser.parse_args()
    slug = os.getenv("RELEASE_APP_SLUG", "")
    base = git("rev-parse", "origin/main")
    version = read_version(base)
    if args.operation == "preview":
        prepare(base, version, preview=True, slug=slug)
        return
    if not slug:
        raise ValueError("Set RELEASE_APP_SLUG before enabling auto-release")
    sha = prepared_commit(version, slug)
    if args.operation == "merged":
        if sha and sha == os.environ["MERGE_SHA"]:
            tag_release(version, sha, slug, retry=True)
        return
    if args.operation == "completed":
        if (
            sha
            and os.environ["VALIDATION_SHA"] == sha
            and os.environ["VALIDATION_BRANCH"] == f"{VERIFY_PREFIX}v{version}"
        ):
            tag_release(version, sha, slug, retry=False)
        return
    current_tag = ref_sha(f"tags/v{version}")
    published = cast("ReleaseState | None", api(f"releases/tags/v{version}", missing=True))
    if not current_tag or not published or published["draft"] or registry(version) is None:
        if not sha:
            raise ValueError(
                f"Resolve pending version {version} through the manual release workflow"
            )
        if args.operation == "prepare":
            summary(f"Version {version} is pending. Resume its validation or publication.")
            return
        tag_release(version, sha, slug, retry=True)
        return
    if args.operation == "resume" and sha:
        branch = f"heads/{VERIFY_PREFIX}v{version}"
        if ref_sha(branch) == sha:
            api(f"git/refs/{branch}", method="DELETE")
    if args.operation == "prepare":
        latest = registry("latest")
        if (
            latest is None
            or latest["version"] != version
            or git("merge-base", "--is-ancestor", f"v{version}^{{commit}}", base) != ""
        ):
            raise ValueError("Current version, main ancestry and npm latest must agree")
        prepare(base, version, preview=False, slug=slug)
    else:
        summary(f"Version {version} is already published.")


if __name__ == "__main__":
    main()
