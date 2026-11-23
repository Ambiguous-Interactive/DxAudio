# DxAudio documentation

DxAudio plays sound effects and music in Unity 2021.3 and newer. Designers tune assets, and game code calls the players.

| Section         | Where to go                                                                                                                 |
| --------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Getting Started | [Install](install.md), [Quickstart](quickstart.md), [BasicUsage sample](../Samples~/BasicUsage/)                            |
| Guides          | [Sound effects](cues.md), [Music](music.md), [Ducks](ducks.md), [DSP effects](effects.md)                                   |
| Concepts        | [Voice pool](cues.md#how-a-voice-is-chosen), [Clip lifetime](cues.md#clip-lifetime), [Duck composition](ducks.md#composing) |
| Editor Tools    | [Authoring and Play Mode diagnostics](editor-tools.md)                                                                      |
| Troubleshooting | [Playback, drops and samples](troubleshooting.md)                                                                           |

## Concepts

- **Cue.** One sound effect with interchangeable takes, volume, pitch jitter, a rate limit, a priority and 3D falloff.
- **SFX library.** The cues a player looks up by key and the number of voices it owns.
- **Track.** One piece of music with a clip, a loop flag, a trim and a start offset under a context name.
- **Music library.** The tracks a player looks up by context and the fade times.
- **Duck.** A reason to lower and filter the music, with `One` or `Many` holders.
- **Voice.** One `AudioSource` the player owns and reuses. Playing a cue claims a voice.
- **Chain.** An ordered list of effects a cue, a track or a player's bus runs on its voices.

## Reference

- [Cue fields](cues.md#cue-fields)
- [Music events and state](music.md#events-and-state)
- [DxMessaging messages](integration.md)

## See also

- [Development](development.md)
- [Changelog](../CHANGELOG.md)
