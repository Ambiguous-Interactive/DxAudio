---
doc_type: explanation
---

# Music playback

## Context switches

Asking for the music that already plays does nothing, so a door trigger or a reload does not restart the track. Two
tracks with the same clip, loop flag, trim, start offset, chain and graph count as the same music, so switching between
their contexts keeps the playhead. A different chain or graph changes the signal path and restarts the track.

## Clip load type

Unity stores the load type on the clip. Every cue and track that uses the clip shares that setting.

- **Auto** (default) streams a clip longer than 30 seconds and decompresses a clip of 30 seconds or less.
- **Decompress On Load** keeps the whole decoded clip in memory, so playback never touches storage.
- **Compressed In Memory** keeps the compressed clip and decodes it as it plays.
- **Streaming** reads from disk as it plays, and reads again at every loop.

Measured in the host editor on Unity 6000.6.7f1 with 44.1 kHz stereo clips:

| Load type            | 20 s clip | 240 s clip | Load call   |
| -------------------- | --------- | ---------- | ----------- |
| Decompress On Load   | 3.4 MiB   | 40.4 MiB   | 31 / 367 ms |
| Compressed In Memory | 0.4 MiB   | 4.3 MiB    | 1 ms        |
| Streaming            | 0.2 MiB   | 0.2 MiB    | 1–2 ms      |

A decompressed clip holds about 170 KiB for each second of music, so a long track's memory grows with its length. A
streaming clip keeps a small buffer and pays a disk read at each loop. The editor renders no audio, so the table covers
memory and the main-thread load call. A mismatched clip still plays, because the import setting
wins; **Tools > Wallstop Studios > DxAudio > Validate Libraries** reports the mismatch, and the track inspector applies the track's choice.

## Fades and low-pass filters

The player fades with its own ramp on the voice's `AudioSource.volume`. It writes the low-pass and high-pass cutoffs to
`AudioLowPassFilter` and `AudioHighPassFilter` components on the voice. A duck's filter settings take effect through
those components. A duck that changes only volume leaves the filters as they are.

## Mixer groups

A music library, an SFX library, and a cue can each name an `AudioMixerGroup`. A game keeps its own Master, Music and
SFX groups and their sliders. Ducking does not need snapshots, because the player composes attenuation itself and writes
the result to each music voice. `Attenuation` reports the value applied this frame.

## Volume, mute, and pause

The player volume levels for SFX and music default to 1 and clamp to the range from 0 to 1. They scale new and active voices and survive library
changes. Music level changes ramp over 10 ms of update time and multiply the track level, fade, and duck gain.

Muting detaches output and keeps state, so loops and music resume in time. Pause holds each voice at its playhead
and keeps loops, leases and mute state. A cue or track started during the pause waits at its start.
See [voice selection](cue-concepts.md#how-a-voice-is-chosen) for how paused cues share the pool.
The player pause settings work independently of `AudioListener.pause`.

For a cue or music track whose clip you load or unload yourself, see [Clip lifetime](cue-concepts.md#clip-lifetime).

See [Music](music.md) to set up a player.
