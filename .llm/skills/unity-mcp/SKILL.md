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

The project pin must be the ENCLOSING Unity project, not `Packages/`. Pointing it at the package silently unpins the server.

## Troubleshooting

- **Unreachable:** is the bridge running on the host? Does `host.docker.internal` resolve in the container? The devcontainer adds it with `--add-host`.
- **`Not a Unity project`:** that invoked the Unity CLI, not the npm script. Use `npm run unity:mcp:host`.
- **Port already in use:** the bridge moves to the next free port and persists `UNITY_MCP_HTTP_PORT`. Delete a VS Code forwarded port that shadows the host listener.
- **Empty tool list:** the Pipeline package has not loaded, or the wrong editor answered. Run `npm run unity:mcp:doctor`.
- **Editor not ready:** editor-dependent tools fail until an editor is open for the pinned project. `unity open <project>`.
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
