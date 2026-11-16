# Troubleshooting

Start with the **Audio Diagnostics** overlay. Add the component, assign both players, and tick **Enabled** in Play mode.
It lists every busy voice with its priority and loop lease, every live duck and its holder, and every dropped cue by
reason.

## Nothing plays

| Check                     | How                                                                 |
| ------------------------- | ------------------------------------------------------------------- |
| Play mode                 | Unity's edit mode produces no sound. Enter Play mode before testing |
| A listener                | The scene needs one active `AudioListener`, usually on the camera   |
| The library is assigned   | **Sfx Player > Library** must hold the library that lists the cue   |
| The cue has a clip        | A take without an `AudioClip` is not playable                       |
| The key matches           | `Play("door")` needs a cue whose **Key** is exactly `door`          |
| The listener is not muted | Check `AudioListener.volume` and `AudioListener.pause`              |
| A mixer group is not down | A group named by the library or cue can be at `-80 dB`              |

`Play` returns `false` when the cue is missing, has no clip, or the key does not match. The other rows start the cue and
make no sound. Subscribe to `CueDropped` to read the reason for a refusal.

## A cue is dropped

| Reason            | Cause                                                                | Fix                                                        |
| ----------------- | -------------------------------------------------------------------- | ---------------------------------------------------------- |
| `Throttled`       | The cue played inside its **Minimum Interval Seconds**               | Lower the interval. A burst inside the limit is expected.  |
| `NoVoice`         | Every voice plays a cue of equal or higher priority, or holds a loop | Raise **Voice Count**, or give this cue a higher priority. |
| `LoopLimit`       | The library's looping voice limit is reached                         | End another loop, or raise **Looping Voice Limit**.        |
| `NotPlayable`     | Unknown key, no clip, or the clip was released before the play       | Check the key and the take. Load the clip before playing.  |
| `WrongPlayMethod` | A looping cue sent to `Play`, or a one-shot sent to `TryPlayLooping` | Use `TryPlayLooping` for a cue with **Loop** set.          |

## A loop does not stop

`StopLoop` takes the handle `TryPlayLooping` returned. A handle from an earlier loop, or from a voice another cue
reused, matches nothing, so it stops nothing and returns `false`. Store the handle from the call that started the loop
and clear it after a stop. `StopAll` ends everything, loops included.

## The music is silent

| Check                         | How                                                          |
| ----------------------------- | ------------------------------------------------------------ |
| `_music.Muted`                | Muting keeps state but detaches output                       |
| `_music.IsPlaying`            | False when no track, no clip, or the clip's data is unloaded |
| `PlayContext` returned false  | The library has no playable track for that context           |
| A duck holds the music        | `DescribeDucks()` lists every live duck and its holder       |
| The composed volume is near 0 | Read `TargetAttenuation.volumeScale`                         |

Asking for the context again returns `true` and starts the track once it has a clip again. A track whose clip was
destroyed stops and can start again once the player has a clip.

If the music stays quiet after dialogue ends, a `Many` duck is still held. Release every holder, or let a destroyed
holder be released for you on the next frame.

## A duck will not release

`Release` releases the lease `Duck` returned. A lease from a different player, or one already released, releases
nothing. `Unduck` releases a duck by asset and holder. A `One` duck ignores the holder and ends on one release; a `Many`
duck ends when the last holder releases.

## Sounds allocate during play

`SfxPlayer.Play` allocates nothing on the steady path. A `CueDropped` handler allocates only when a cue drops, and the
diagnostics overlay samples only while **Enabled** is set. If you see per-frame garbage, turn the overlay off and
retest.

## The sample scene will not open

The npm payload carries no sample. Install the package from git or a checkout to list it in the Package Manager
**Samples** tab, or copy `Samples~/BasicUsage` into the project's `Assets/` folder; Unity does not show folders whose
name ends in `~`. A copy under `Assets/` keeps its script references.

## More

- [README](../README.md) for the full reference.
- [Cues](cues.md) for pool and priority rules.
- [Ducks](ducks.md) for composition rules.
