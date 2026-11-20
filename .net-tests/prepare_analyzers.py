#!/usr/bin/env python3
"""Fetch the exact development-only unity-helpers diagnostic analyzer."""

import argparse
import hashlib
import io
import tempfile
import urllib.request
import zipfile
from collections.abc import Callable
from pathlib import Path
from typing import cast

UNITY_HELPERS_COMMIT = "072d2dddd79b4a9210aa2812ae5761bf9a5e28c8"
WUH_NAME = "WallstopStudios.UnityHelpers.Analyzers.dll"
WUH_URL = f"https://raw.githubusercontent.com/Ambiguous-Interactive/unity-helpers/{UNITY_HELPERS_COMMIT}/Runtime/Analyzers/{WUH_NAME}"
WUH_SHA256 = "c18903d4f0d327a9702d6bfe692d01ec24e89428c4d265788b7054c9dea5b6fb"
NUNIT_NAME = "nunit.analyzers.dll"
NUNIT_VERSION = "3.3.0"
NUNIT_URL = f"https://api.nuget.org/v3-flatcontainer/nunit.analyzers/{NUNIT_VERSION}/nunit.analyzers.{NUNIT_VERSION}.nupkg"


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def download(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=30) as response:  # noqa: S310 - pinned HTTPS inputs.
        return cast("bytes", response.read())


def stage(path: Path, expected: str, get_payload: Callable[[], bytes]) -> None:
    if path.is_file() and sha256(path.read_bytes()) == expected:
        return
    payload = get_payload()
    if sha256(payload) != expected:
        raise SystemExit(f"Checksum mismatch for {path.name}")
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as handle:
        temporary = Path(handle.name)
        handle.write(payload)
    try:
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def unity_nunit() -> bytes:
    with zipfile.ZipFile(io.BytesIO(download(NUNIT_URL))) as archive:
        return archive.read("analyzers/dotnet/cs/nunit.analyzers.dll")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument(
        "--unity-nunit", action="store_true", help="also stage the Unity 2021.3 compatible analyzer"
    )
    options = parser.parse_args()
    stage(options.output_dir / WUH_NAME, WUH_SHA256, lambda: download(WUH_URL))
    if options.unity_nunit:
        stage(
            options.output_dir / NUNIT_NAME,
            "7d44d82f5cc0b395e4f0c184aff93e190128ea7c7c13066c2aa31b064904909e",
            unity_nunit,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
