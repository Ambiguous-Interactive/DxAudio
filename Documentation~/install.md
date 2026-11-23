---
doc_type: how-to
---

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
        "com.wallstop-studios.dxaudio": "0.4.0"
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
https://github.com/Ambiguous-Interactive/DxAudio.git#v0.4.0
```

## From a tarball

Download `com.wallstop-studios.dxaudio-<version>.tgz` from a
[release](https://github.com/Ambiguous-Interactive/DxAudio/releases), then choose **+ > Add package from tarball**.

## Dependencies

Install DxMessaging only if you want to drive audio with its messages. The optional [DxMessaging integration](integration.md) compiles only when you install DxMessaging.

## What the package contains

See [package contents](reference.md#what-the-package-contains) for what each install includes.

## Next

Continue with the [quickstart](quickstart.md).
