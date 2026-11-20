"""Build deterministic Unity archives and publish verified release files."""

from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import io
import re
import subprocess
import tarfile
import time
from pathlib import Path
from typing import TypedDict, cast

from package_check import NAME, archive_paths, unity_visible
from release_service import api, registry, run


class Release(TypedDict):
    id: int
    tag_name: str
    draft: bool


class Asset(TypedDict):
    id: int
    name: str
    digest: str | None


def read_member(archive: tarfile.TarFile, member: str) -> bytes:
    stream = archive.extractfile(member)
    if stream is None:
        raise ValueError(f"Missing archive file {member}")
    with stream:
        return stream.read()


def build_unitypackage(source: Path, output: Path) -> None:
    paths, problems = archive_paths(source)
    if problems:
        raise ValueError("\n".join(problems))
    visible = {path for path in paths if unity_visible(path)}
    seen: set[bytes] = set()
    with (
        tarfile.open(source) as archive,
        output.open("wb") as stream,
        gzip.GzipFile(filename="", fileobj=stream, mode="wb", mtime=0) as compressed,
        tarfile.open(fileobj=compressed, mode="w|", format=tarfile.USTAR_FORMAT) as package,
    ):
        for path in sorted(visible):
            if not path.endswith(".meta"):
                if f"{path}.meta" not in visible:
                    raise ValueError(f"Missing Unity meta for {path}")
                continue
            asset_path = path.removesuffix(".meta")
            meta = read_member(archive, f"package/{path}")
            match = re.search(rb"(?m)^guid: ([0-9a-fA-F]{32})\s*$", meta)
            if match is None or match[1].lower() in seen:
                raise ValueError(f"Invalid or duplicate Unity GUID in {path}")
            guid = match[1].lower().decode()
            seen.add(match[1].lower())
            entries: dict[str, bytes] = {}
            if asset_path in visible:
                entries["asset"] = read_member(archive, f"package/{asset_path}")
            elif not re.search(rb"(?m)^folderAsset: yes\s*$", meta):
                raise ValueError(f"Missing Unity asset for {path}")
            entries["asset.meta"] = meta
            entries["pathname"] = f"Packages/{NAME}/{asset_path}".encode()
            for name, data in entries.items():
                member = tarfile.TarInfo(f"{guid}/{name}")
                member.size = len(data)
                package.addfile(member, io.BytesIO(data))
    if not seen:
        raise ValueError("Unity package is empty")


def npm_integrity(path: Path) -> str:
    return "sha512-" + base64.b64encode(hashlib.sha512(path.read_bytes()).digest()).decode()


def publish_npm(version: str, tarball: Path, prerelease: bool) -> None:
    entry = registry(version)
    if entry is None:
        if registry("latest") is None:
            raise ValueError("Create the npm package and configure trusted publishing first")
        flags = ["--tag", "next"] if prerelease else []
        run("npm", "publish", str(tarball), "--access", "public", *flags)
    expected = npm_integrity(tarball)
    for attempt in range(6):
        entry = registry(version)
        if entry is not None:
            if entry["dist"]["integrity"] != expected:
                raise ValueError(f"npm integrity mismatch for {NAME}@{version}")
            return
        if attempt < 5:
            time.sleep(5)
    raise ValueError(f"npm has not returned {NAME}@{version}; resume this release")


def github_release(operation: str, tag: str, tarball: Path, prerelease: bool) -> None:
    pages = cast("list[list[Release]]", api("releases?per_page=100", paginate=True))
    matches = [release for page in pages for release in page if release["tag_name"] == tag]
    if len(matches) > 1:
        raise ValueError(f"Multiple GitHub Releases for {tag}")
    unity = tarball.with_suffix(".unitypackage")
    files = [
        tarball,
        tarball.with_suffix(".tgz.sha256"),
        unity,
        unity.with_suffix(".unitypackage.sha256"),
    ]
    if not matches:
        if operation != "stage":
            raise ValueError(f"Missing GitHub Release for {tag}")
        flags = ["--prerelease"] if prerelease else []
        run(
            "gh",
            "release",
            "create",
            tag,
            "--draft",
            "--verify-tag",
            "--title",
            f"DxAudio {tag[1:]}",
            "--notes-file",
            str(tarball.parent / "notes.md"),
            *flags,
        )
        github_release(operation, tag, tarball, prerelease)
        return
    release = matches[0]
    assets = {
        asset["name"]: asset
        for page in cast(
            "list[list[Asset]]", api(f"releases/{release['id']}/assets?per_page=100", paginate=True)
        )
        for asset in page
    }
    uploaded = False
    for path in files:
        asset = assets.get(path.name)
        if asset is None:
            if operation != "stage" or not release["draft"]:
                raise ValueError(f"Missing GitHub Release asset {path.name}")
            run("gh", "release", "upload", tag, str(path))
            uploaded = True
        else:
            digest = asset.get("digest")
            if digest is None:
                downloaded = subprocess.check_output(  # noqa: S603 - fixed authenticated GitHub asset download
                    [  # noqa: S607 - pinned runner GitHub CLI
                        "gh",
                        "api",
                        "-H",
                        "Accept: application/octet-stream",
                        f"repos/{{owner}}/{{repo}}/releases/assets/{asset['id']}",
                    ]
                )
                digest = "sha256:" + hashlib.sha256(downloaded).hexdigest()
            if digest != "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest():
                raise ValueError(f"GitHub Release asset digest mismatch for {path.name}")
    if operation == "stage" and uploaded:
        github_release("verify", tag, tarball, prerelease)
    if operation == "finalize" and release["draft"]:
        run("gh", "release", "edit", tag, "--draft=false")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("build", "stage", "npm", "finalize"))
    parser.add_argument("tarball", type=Path)
    parser.add_argument("--tag")
    parser.add_argument("--prerelease", action="store_true")
    args = parser.parse_args()
    if args.operation == "build":
        unity = args.tarball.with_suffix(".unitypackage")
        build_unitypackage(args.tarball, unity)
        unity.with_suffix(".unitypackage.sha256").write_text(
            f"{hashlib.sha256(unity.read_bytes()).hexdigest()}  {unity.name}\n"
        )
    elif args.tag is None:
        parser.error("--tag is required to publish")
    elif args.operation == "npm":
        publish_npm(args.tag[1:], args.tarball, args.prerelease)
    else:
        github_release(args.operation, args.tag, args.tarball, args.prerelease)


if __name__ == "__main__":
    main()
