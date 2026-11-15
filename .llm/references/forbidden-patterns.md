# Forbidden Patterns

Each entry names the banned shape and the compliant alternative.

| Banned                                                                               | Use instead                                                          |
| ------------------------------------------------------------------------------------ | -------------------------------------------------------------------- |
| `UnityEngine` types in `Runtime/Core` (except `UnityObjects.cs`)                     | Engine-free math and state; put Unity calls in `Players` or `Assets` |
| `using System.Linq` in `Runtime/` or `Tests/`                                        | Plain loops over the concrete collection                             |
| `foreach` over a `string` in production                                              | Counted `for` loop                                                   |
| `?.`, `??`, `is null`, `!`, or implicit bool on a `UnityEngine.Object`               | `== null` / `!= null`                                                |
| `Assert.IsNull` / `Assert.IsNotNull`                                                 | `Assert.That(value == null)` / `Assert.That(value != null)`          |
| `count > 0`, `a >= b` against the left operand                                       | `0 < count`, `b <= a`                                                |
| `var` in production code                                                             | Explicit type                                                        |
| Expression-bodied members or lambdas                                                 | Block bodies with braces                                             |
| A second comment above a revised one                                                 | Edit the original comment in place                                   |
| A per-play allocation in `SfxPlayer.Play`                                            | Pre-sized pool, cached selection, reused buffers                     |
| Logging per drop, duck, or voice in the steady state                                 | Counters and an opt-in diagnostics surface                           |
| Stealing a looping voice                                                             | Dropped cue with `LoopLimit` or skip that voice                      |
| Equal-priority preemption                                                            | Drop the incoming cue                                                |
| A duck that overwrites another duck's attenuation                                    | Compose to the most restrictive value                                |
| A stale, default, or foreign lease releasing a duck                                  | Ignore it; leases carry a generation                                 |
| Renaming a serialized field without `[FormerlySerializedAs]`                         | Keep the old name or migrate                                         |
| Committing `.env.local`, generated MCP configs, `progress/`, `PLAN.md`, or `GOAL.md` | Gitignore them                                                       |
| Committing a `.meta` for a dot path                                                  | Dot paths are invisible to Unity                                     |
| `-quit` with `-runTests`                                                             | Omit `-quit`; the test framework ends the process                    |
| `sudo npm`                                                                           | Rerun `uv run .devcontainer/install_npm_tools.py` or rebuild         |
| `NPM_TOKEN` in the release workflow                                                  | npm trusted publishing through GitHub OIDC                           |
