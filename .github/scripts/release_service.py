"""Read GitHub release state and distinguish missing records from service errors."""

from __future__ import annotations

import json
import os
import subprocess
from typing import TypedDict, cast
from urllib.error import HTTPError
from urllib.request import urlopen

from package_check import NAME


class Distribution(TypedDict):
    integrity: str


class PublishedVersion(TypedDict):
    version: str
    dist: Distribution


def run(*args: str, data: str | None = None) -> str:
    result = subprocess.run(args, input=data, text=True, capture_output=True, check=False)  # noqa: S603 - argument arrays from release commands
    if result.returncode:
        raise RuntimeError(
            result.stderr.strip() or f"{args[0]} failed with exit code {result.returncode}"
        )
    return result.stdout.strip()


def api(
    path: str,
    *,
    method: str = "GET",
    body: dict[str, object] | None = None,
    missing: bool = False,
    paginate: bool = False,
) -> object:
    endpoint = f"repos/{os.environ['GITHUB_REPOSITORY']}/{path}"
    args = ["gh", "api", "-H", "X-GitHub-Api-Version: 2026-03-10", "--method", method, endpoint]
    if paginate:
        args += ["--paginate", "--slurp"]
    if body is not None:
        args += ["--input", "-"]
    try:
        output = run(*args, data=json.dumps(body) if body is not None else None)
    except RuntimeError as error:
        if missing and "(HTTP 404)" in str(error):
            return None
        raise
    return json.loads(output) if output else None  # JSON is decoded at the caller boundary


def registry(version: str) -> PublishedVersion | None:
    try:
        with urlopen(f"https://registry.npmjs.org/{NAME}/{version}", timeout=20) as response:
            return cast("PublishedVersion", json.load(response))
    except HTTPError as error:
        if error.code == 404:
            return None
        raise
