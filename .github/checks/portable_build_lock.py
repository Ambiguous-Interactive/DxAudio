"""Run the pinned upstream build-lock input contract against DxAudio workflows."""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

POLICY_REPOSITORY = "https://github.com/Ambiguous-Interactive/ambiguous-organization-build-lock.git"
APPROVAL_REF = "refs/remotes/origin/main"


def verify_cleanup_behavior(root: Path, modules: Path, scratch: Path, policy: Path, revision: str) -> None:
    """Run the exact upstream classifier cases and consumer workflow assertions."""
    runtimes = (
        ".github/dist/classify-unity-cleanup-evidence.js",
        ".github/dist/require-confirmed-unity-cleanup.js",
    )
    subprocess.run(["git", "sparse-checkout", "set", "--no-cone", *(f"/{path}" for path in runtimes)],
                   cwd=policy, check=True, timeout=90)
    subprocess.run(["git", "checkout", "--quiet", "--detach", revision], cwd=policy, check=True, timeout=90)
    source = (modules / "tests/test-portable-cleanup-classifier.js").read_text(encoding="utf-8")
    for before, after in (
        ('const root = path.resolve(__dirname, "../..");', "const root = process.argv[2];"),
        ('const yaml = require("yaml");',
         'const yaml = require("node:module").createRequire(path.join(process.argv[2], "package.json"))("yaml");'),
        ('const { resolveBuildLockPin } = require("../resolve-build-lock-pin");\n'
         'const policyCommit = resolveBuildLockPin("require-confirmed-unity-cleanup", root);',
         "const policyCommit = process.argv[3];"),
    ):
        if source.count(before) != 1:
            raise ValueError("the pinned build-lock cleanup fixture no longer matches the adapter")
        source = source.replace(before, after)
    fixture = scratch / "test-portable-cleanup-classifier.cjs"
    fixture.write_text(source, encoding="utf-8")
    subprocess.run(
        ["node", str(fixture), str(root), revision],
        cwd=root, env={**os.environ, "BUILD_LOCK_POLICY_ROOT": str(policy)}, check=True, timeout=45,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--modules", required=True, type=Path)
    options = parser.parse_args()
    sys.path.insert(0, str(options.modules))
    from build_lock_inputs import (
        ACTION_PREFIX,
        collect_calls,
        collect_yaml_files,
        git_environment,
        parse_action_inputs,
        read_action_at_ref,
        validate_build_lock_action_inputs,
    )

    calls = [
        call
        for path in collect_yaml_files(options.root / ".github")
        for call in collect_calls(path, path.relative_to(options.root).as_posix())
    ]
    if not calls or any(not re.fullmatch(r"[a-f0-9]{40}", call.ref) for call in calls):
        parser.error("at least one build-lock action with an immutable reference is required")

    with tempfile.TemporaryDirectory(prefix="dxaudio-build-lock-") as directory:
        scratch = Path(directory)
        policy = scratch / "policy"
        git = {"check": True, "env": git_environment(), "timeout": 90}
        subprocess.run(["git", "init", "-q", str(policy)], **git)
        subprocess.run(["git", "remote", "add", "origin", POLICY_REPOSITORY], cwd=policy, **git)
        subprocess.run(
            ["git", "fetch", "-q", "--filter=blob:none", "--depth=1", "origin", f"main:{APPROVAL_REF}"],
            cwd=policy,
            **git,
        )
        for revision in sorted({call.ref for call in calls}):
            subprocess.run(
                ["git", "fetch", "-q", "--filter=blob:none", "--depth=1", "origin", revision],
                cwd=policy,
                **git,
            )
        result = validate_build_lock_action_inputs(options.root, policy, APPROVAL_REF)
        if result.failures:
            print("\n".join(result.failures), file=sys.stderr)
            return 1

        for call in calls:
            inputs = parse_action_inputs(read_action_at_ref(policy, call.action, call.ref), call.action)
            required = sorted(name for name, needed in inputs.items() if needed)
            if required:
                break
        else:
            raise ValueError("the build-lock actions expose no required input for the control")
        fixture = scratch / "fixture" / ".github" / "workflows" / "control.yml"
        fixture.parent.mkdir(parents=True)

        def check_input_fixture(names: list[str]) -> list[str]:
            body = "".join(f"          {name}: fixture\n" for name in names)
            fixture.write_text(
                f"name: Control\njobs:\n  fixture:\n    steps:\n      - uses: {ACTION_PREFIX}{call.action}@{call.ref}\n        with:\n{body}",
                encoding="utf-8",
            )
            return validate_build_lock_action_inputs(scratch / "fixture", policy, APPROVAL_REF).failures

        if check_input_fixture(required):
            raise ValueError("valid build-lock action input fixture failed")
        faults = check_input_fixture(required[1:] + ["dxaudio-unknown-control"])
        if not any(f"requires input '{required[0]}'" in fault for fault in faults):
            raise ValueError("missing build-lock action input fixture passed")
        if not any("declares no input 'dxaudio-unknown-control'" in fault for fault in faults):
            raise ValueError("unknown build-lock action input fixture passed")
        classifier_pins = {call.ref for call in calls if call.action == "require-confirmed-unity-cleanup"}
        if len(classifier_pins) != 1:
            raise ValueError("cleanup verdict callers must agree on one immutable central policy pin")
        verify_cleanup_behavior(options.root, options.modules, scratch, policy, classifier_pins.pop())
    print(f"Verified {len(calls)} approved build-lock action calls and input controls.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
