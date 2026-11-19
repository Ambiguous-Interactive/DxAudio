# DxAudio

Data-driven sound effects and music for Unity, with no dependencies.

DxAudio plays sound effects from cue assets on a fixed pool of voices, and music from track assets with crossfades,
overrides and ducks. Assets carry the settings, so a designer changes volume, takes and falloff without a code change.
The players never allocate per play, and the engine-free core runs on plain .NET.

Unity 2021.3 or newer.

## Pages

| Page                                  | Read it to                                                   |
| ------------------------------------- | ------------------------------------------------------------ |
| [Install](install.md)                 | Add the package to a project                                 |
| [Quickstart](quickstart.md)           | Play a cue and start music in a few minutes                  |
| [Cues](cues.md)                       | Author sound effects and understand the pool                 |
| [Music](music.md)                     | Set up contexts, fades, overrides and mixer groups           |
| [Effects](effects.md)                 | Shape a cue, a track, a player or the whole mix with a chain |
| [Ducks](ducks.md)                     | Hold the music down for dialogue, menus or cutscenes         |
| [Troubleshooting](troubleshooting.md) | Find why a sound did not play or stayed silent               |

## Sample

`Samples~/BasicUsage` holds one scene that plays a cue, holds a loop, starts music and ducks it. Install from git to
list it in the Package Manager **Samples** tab, or copy the folder into `Assets/` when you work from a checkout.

## Concepts

- **Cue.** One sound effect: interchangeable takes, volume, pitch jitter, a rate limit, a priority and 3D falloff.
- **SFX library.** The cues a player looks up by key, and the number of voices it owns.
- **Track.** One piece of music: a clip, a loop flag, a trim and a start offset, under a context name.
- **Music library.** The tracks a music player looks up by context, plus the fade times.
- **Duck.** A reason to lower and filter the music, with `One` or `Many` holders.
- **Voice.** One `AudioSource` the player owns and reuses. Playing a cue claims a voice.
- **Chain.** An ordered list of effects a cue, a track or a player's bus runs on its voices.

## See also

- [README](../README.md): the full feature list and reference tables.
- [CHANGELOG](../CHANGELOG.md): what changed in each version.
