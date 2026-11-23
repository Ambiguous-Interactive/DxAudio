# Development

## The engine-free core

`Runtime/Core` (the arbiter, rate limit, take selection, duck stack, attenuation, ramps, hand-off and override) uses no
Unity types. Use those types directly to build your own player, and test them on plain .NET:

```bash
npm test
```

The same tests, plus tests of the assets and players, run in Unity's Test Runner when the package is listed under
`testables` in your project manifest.

## Work on the package

The package repo lives under a Unity project's `Packages/` folder. Open this folder in VS Code and choose **Reopen in
Container** for the full toolchain. Copy `.env.local.example` to `.env.local` and fill the keys you need.

On the host, keep the Unity editor open and start the MCP bridge:

```bash
npm run unity:mcp:host
```

For container tests, set `UNITY_MCP_PROJECT` in `.env.local` to the Unity project root on the host.
Run these checks in the container or on the host.

```bash
npm run check:gate      # format, lints, harness self-tests, npm payload
npm run unity:tests     # EditMode via the host bridge in a container, or batch mode on the host
npm run check:analysis  # C# 9 compatibility, static analysis, and Core tests
npm run check:upstream  # pinned unity-helpers checks and analyzer fixtures
```

See [.devcontainer/README.md](../.devcontainer/README.md) for the container lifecycle and
[.llm/context.md](../.llm/context.md) for agent rules.
