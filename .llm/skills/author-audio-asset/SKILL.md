---
name: author-audio-asset
description: Create and edit DxAudio ScriptableObject assets - SfxCue, SfxVariant, SfxLibrary, MusicTrack, MusicLibrary, MusicDuck - through the Create menu or the inspector, including serialized-field compatibility and .meta companions. Use when adding an asset type, a field, or a sample scene.
metadata:
  category: Feature
---

# Author Audio Assets

## Types

| Asset          | Menu                         | Holds                                                        |
| -------------- | ---------------------------- | ------------------------------------------------------------ |
| `SfxCue`       | Create > DxAudio > Sfx Cue   | Interchangeable takes, volume, pitch jitter, rate limit, priority, 3D falloff |
| `SfxVariant`   | part of a cue                | Clip plus start offset                                       |
| `SfxLibrary`   | Create > DxAudio > Sfx Library | Keyed cues and the voice count                             |
| `MusicTrack`   | Create > DxAudio > Music Track | Clip, trim, start point, context                           |
| `MusicLibrary` | Create > DxAudio > Music Library | Keyed tracks and fade times                              |
| `MusicDuck`    | Create > DxAudio > Music Duck | Volume, low/high-pass, apply/release, holder policy         |

## Rules

1. Every asset type is `[CreateAssetMenu(menuName = "DxAudio/...")]` and lives in `Runtime/Assets`.
2. Serialized fields are a public contract. Renaming one needs `[FormerlySerializedAs]` and a `CHANGELOG.md` entry.
3. New fields need a sane default for assets already on disk. A `null` clip is empty data, not a crash.
4. Libraries validate keys on `OnValidate` and warn once per duplicate. Do not log every frame.
5. Sample content belongs in `Samples~` only if it does not ship in the npm payload; otherwise keep tests self-contained with generated clips.
6. Visible asset, folder, and script files need `.meta` companions. See [create-unity-meta](./create-unity-meta/SKILL.md).

## Inspector work

The package has no `Editor/` assembly yet. Authoring is through Unity's default inspector plus the Create menu. When an editor assembly is added, it must not change runtime
behavior and must stay out of the `Runtime` assembly references.

## Tests

- Asset behavior: `Tests/Editor/Unity/LibraryTests.cs` (`AnSfxLibraryFindsPlayableCuesByKey`, `AnSfxLibraryWarnsAboutADuplicateKey`, ...).
- Cue start-clamp behavior: `ACueClampsItsStartAndIgnoresItForAnEngineLoop`.

## Related

- [voice-arbitration](./voice-arbitration/SKILL.md) - how a cue reaches a voice
- [duck-composition](./duck-composition/SKILL.md) - duck asset fields
