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
| `npm run unity:tests`          | container | EditMode tests through the bridge                            |
| `npm run unity:mcp:host -- --stop` | host  | Stop the bridge                                              |

## Play mode

**Never enter or drive play mode through the bridge.** Play mode stops the editor answering until a human leaves it,
and on 2026-11-17 a PlayMode test run aborted the pinned editor: the crash dump reports `STATUS_FATAL_APP_EXIT`, the
`test_run` call timed out after 180 s, and the pinned instance disappeared until the editor was restarted by hand. The
run also left `Assets/DxAudioProbe` behind.

The bridge enforces this. It answers a play-mode call with an error and forwards nothing, unless the host sets
`UNITY_MCP_ALLOW_PLAY_MODE=1` and restarts the bridge for a supervised run. The refused calls are
`editor_playmode_start`, `editor_playmode_pause`, `editor_playmode_set_run_in_background`, `runtime_quit`,
`runtime_set_target_frame_rate`, `runtime_set_time_scale`, `runtime_simulate_key`, `runtime_simulate_pointer`, and
`test_run` with `mode` `playmode` or `all`. Read-only and recovery calls stay allowed: `editor_playmode_stop`,
`editor_playmode_list_instances`, `runtime_status`, `test_status`, and `test_run` with `mode` `editor`.

What to do instead:

- EditMode and asset work: this bridge, as before.
- A PlayMode suite: `uv run scripts/run_unity_validation.py --suite playmode ...`, which starts its own batchmode
  editor process and never touches the bridge. Batchmode renders no audio, so an audible measurement still needs a
  player build and a person at the machine.
- A PlayMode test never writes assets: no `AssetDatabase` write, no file under `Assets/`, no `SaveAndReimport`. An
  import during play mode forces a synchronous refresh inside the running player loop, which is where the 2026-11-17
  run died. Generate fixtures before play mode, or in a player build.

### When the bridge answers but the editor is gone

`unity-cli-status` reporting `No Pipeline instance found for project: ...` while the bridge still answers means the
editor exited or aborted. Stop issuing calls and collect the evidence first:

1. `Logs/Editor.log` on the host holds the tail of the session, and `Logs/Editor-prev.log` the session before it.
2. `%LOCALAPPDATA%\Temp\Unity\Editor\Crashes\Crash_<utc>_<serial>` holds `crash.dmp` and a startup copy of the log.
   The folder name is the crash time, which is how a crash is told apart from a kill.
3. `npm run unity:mcp:doctor` warns with the same path and never restarts anything by itself.

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

- [capture-unity-state](./capture-unity-state/SKILL.md) - reading editor state safely
- [.llm/mcp/README.md](../../mcp/README.md) - full bridge reference
