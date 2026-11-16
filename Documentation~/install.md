# Install

DxAudio supports Unity 2021.3 and newer and needs no other package.

## From npm

Add the scoped registry and the package to `Packages/manifest.json`:

```json
{
    "scopedRegistries": [
        {
            "name": "npmjs",
            "url": "https://registry.npmjs.org",
            "scopes": ["com.wallstop-studios"]
        }
    ],
    "dependencies": {
        "com.wallstop-studios.dxaudio": "0.3.0"
    }
}
```

Package Manager lists new versions under **My Registries**. If the manifest already has a registry for
`https://registry.npmjs.org`, add the `com.wallstop-studios` scope to it instead.

## From OpenUPM

With the [OpenUPM CLI](https://openupm.com/docs/getting-started-cli.html), run:

```bash
openupm add com.wallstop-studios.dxaudio
```

Without the CLI, add a scoped registry for `https://package.openupm.com` with the scope `com.wallstop-studios.dxaudio`.

## From git

Open **Window > Package Manager**, choose **+ > Add package from git URL**, and enter:

```text
https://github.com/Ambiguous-Interactive/DxAudio.git#v0.3.0
```

## From a tarball

Download `com.wallstop-studios.dxaudio-<version>.tgz` from a
[release](https://github.com/Ambiguous-Interactive/DxAudio/releases), then choose **+ > Add package from tarball**.

## What the package contains

| Folder            | Ships | Contents                                                           |
| ----------------- | ----- | ------------------------------------------------------------------ |
| `Runtime/`        | Yes   | Cues, tracks, ducks, libraries and the two players                 |
| `Editor/`         | Yes   | Cue, duck and music track inspectors, library validation           |
| `Tests/`          | Yes   | EditMode tests, run only when the package is listed in `testables` |
| `Documentation~/` | No    | These pages                                                        |
| `Samples~/`       | No    | The `BasicUsage` sample                                            |

Unity ignores folders whose name ends in `~` and npm does not pack them, so neither adds to a build or a download. A
registry or tarball install has no sample; install from git, or copy `Samples~/BasicUsage` from the repository.

## Next

Continue with the [quickstart](quickstart.md).
