---
name: unity-mcp
description: Drive the host Unity editor from agents through the official Unity CLI MCP server and its HTTP bridge - start the bridge, sync client configs, probe readiness, run tests, and diagnose port or project-pin failures. Use when Unity MCP is unreachable, MCP configs need regenerating, or an agent must run EditMode tests or eval editor state.
metadata:
  category: Feature
---

# Unity MCP

Unity runs on the host. The official Unity CLI ships the MCP server (`unity mcp`, built on `com.unity.pipeline`). Container agents reach it over HTTP through
`scripts/unity_mcp_host.py`, started by `npm run unity:mcp:host`.

## One-time host setup

```bash
cd C:/Code/DxAudio
unity pipeline install
npm install            # workspace root of the package repo, once
```

The host project must declare `com.unity.pipeline` in `Packages/manifest.json`. The generated project does.

## Commands

| Command                        | Where     | Does                                                        |
| ------------------------------ | --------- | ----------------------------------------------------------- |
| `npm run unity:mcp:host`       | host      | Spawns `unity mcp`, serves streamable HTTP on port 9020      |
| `npm run mcp:sync`             | either    | Writes the six frontend configs, idempotent                  |
| `npm run mcp:doctor`           | either    | Credentials, config flavor, endpoint and stdio checks        |
| `npm run unity:mcp:doctor`     | either    | Project pin, pipeline package, tool roundtrip                |
| `npm run unity:tests`          | host      | EditMode tests in a separate batchmode editor                |
| `npm run unity:mcp:host -- --stop` | host  | Stop the bridge                                              |

## Play mode

Play mode through the bridge is a hazard to the bridge, not to the editor: the pinned editor stops answering every
call until a human leaves play mode. The CLI adds a second trap: `test run` takes `mode` `all` when the caller omits
it, so a plain `test_run {"filter": ...}` runs PlayMode tests.

On 2026-11-17 a PlayMode test run also ended in a fatal editor abort (`STATUS_FATAL_APP_EXIT` in the crash dump, the
CLI timing out after 180 s). That abort is not attributed: the host's editor has crashed four times in three days, and
three of those came from sessions that never entered play mode. Two hit the same instruction in Unity's own code
(`Unity.dll+0x2F27E54`, a read at `0xA4`) and one hit Boehm GC. The dump from the PlayMode run is a different mode, and
the log that named it rotated away with the restart.

What the bridge does, and why:

- `test_run` must say `mode` `editor`. Without it the CLI runs `all`, so the bridge refuses the call rather than run
  tests nobody asked for.
- `editor_playmode_start`, `editor_playmode_pause`, `editor_playmode_set_run_in_background`, and the `runtime_*`
  mutators are refused, because they stop the bridge answering. `editor_playmode_stop`,
  `editor_playmode_list_instances`, `runtime_status`, and `test_status` stay allowed so a stuck editor is recoverable.
- `UNITY_MCP_ALLOW_PLAY_MODE=1` on the host restores every refused call for a supervised run. The bridge reads it at
  start, so restart the bridge after a change.

What to do instead:

- EditMode and asset work: this bridge.
- A PlayMode suite: `uv run scripts/run_unity_validation.py --suite playmode ...`, which starts its own batchmode
  editor process and never touches the bridge. Batchmode renders no audio, so an audible measurement still needs a
  player build and a person at the machine.
- A PlayMode test never imports or writes an asset. An import inside play mode runs a synchronous asset refresh in the
  running player loop, and this host has lost the editor twice while it imported assets. `scripts/lint_playmode_sources.py`
  fails a `Tests/PlayMode` source that names `AssetDatabase`, `AssetImporter`, `SaveAndReimport`, `ImportAsset`, or a
  file write. Author the fixture before play mode, or measure it in a player build.

### When the bridge answers but the editor is gone

`unity-cli-status` reporting `No Pipeline instance found for project: ...` while the bridge still answers means the
editor exited or aborted. Stop issuing calls and collect the evidence first:

1. `Logs/Editor.log` on the host holds the tail of the session, and `Logs/Editor-prev.log` the session before it.
   Unity overwrites `Editor.log` on the next start, so copy the tail before a restart. `bridge_diagnostics` already
   returns it as `editorLogTail`, and the doctor prints its last lines, because the bridge outlives the editor.
2. `%LOCALAPPDATA%\Temp\Unity\Editor\Crashes\Crash_<utc>_<serial>` holds `crash.dmp` and a startup copy of the log.
   The folder name is the crash time in UTC, which is how a crash is told apart from a kill.
3. `crash.dmp` is a minidump: the exception code names the failure (`0xC0000005` is a fault, `0x40000015` is an
   abort). The exception address and the module list tell you whether Unity or a driver faulted.
4. `npm run unity:mcp:doctor` warns with the same path and never restarts anything by itself.

Then restart the editor by hand, and do not repeat the command that took it down.

## Settings

Read from the environment, falling back to the gitignored `.env.local`:

| Variable               | Purpose                                                         |
| ---------------------- | --------------------------------------------------------------- |
| `UNITY_MCP_PROJECT`    | Project root (contains `ProjectSettings/ProjectVersion.txt`)    |
| `UNITY_PROJECT_PATH`   | Alias honored by the Unity CLI                                  |
| `UNITY_MCP_HTTP_PORT`  | Bridge port, default 9020; busy ports move to the next free one  |
| `UNITY_MCP_HTTP_URL`   | Endpoint override read by sync and both doctors                 |
| `UNITY_MCP_TOKEN`      | Optional bearer token                                           |
| `UNITY_MCP_TIMEOUT_MS` | Per-request timeout, default 180000                             |
| `UNITY_MCP_ALLOW_PLAY_MODE` | `1` lets the bridge forward play-mode calls; unset refuses them |

The project pin must be the ENCLOSING Unity project, not `Packages/`. Pointing it at the package silently unpins the server.

## Troubleshooting

- **Unreachable:** is the bridge running on the host? Does `host.docker.internal` resolve in the container? The devcontainer adds it with `--add-host`.
- **`Not a Unity project`:** that invoked the Unity CLI, not the npm script. Use `npm run unity:mcp:host`.
- **Port already in use:** the bridge moves to the next free port and persists `UNITY_MCP_HTTP_PORT`. Delete a VS Code forwarded port that shadows the host listener.
- **Empty tool list:** the Pipeline package has not loaded, or the wrong editor answered. Run `npm run unity:mcp:doctor`.
- **Editor not ready:** editor-dependent tools fail until an editor is open for the pinned project. `unity open <project>`.
- **Bridge answers, no instance:** the editor exited or aborted. Read the crash record first ([Play mode](#play-mode)),
  then restart the editor by hand; a crash dump names the time, and the log names the tail.
- **`WARNING: Parameter 'NoTypeInformation' is obsolete`:** host shell noise, not the bridge. `npm run unity:mcp:host`
  spawns `uv run` then `scripts/unity_mcp_host.py`, and neither one calls PowerShell: the same command under `cmd /c` and
  under `pwsh` with the interactive profile prints no warning. The line comes from a PowerShell 7 terminal hook that runs
  after each command, so fix that hook instead of the repository.

## When no bridge runs

Use the direct batchmode runner:

```bash
uv run scripts/run_unity_validation.py \
  --host-project C:/Code/DxAudio \
  --unity-executable "C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe" \
  --unity-version 6000.6.7f1 \
  --suite editmode \
  --output-directory C:/Temp/DxAudioValidation
```

It omits `-quit` on purpose; with `-quit`, Unity exits before the test runner starts.

## Related

- [capture-unity-state](../capture-unity-state/SKILL.md) - reading editor state safely
- [.llm/mcp/README.md](../../mcp/README.md) - full bridge reference
- [host-environment](../../references/host-environment.md) - the host's hardware, its crash record, and how to read a
  minidump before a restart rotates the log
