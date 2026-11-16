# Basic usage sample

One scene that shows the four things a game does with DxAudio:

- play a one-shot cue;
- hold a looping voice, then stop it with its handle;
- start music on a context, with a fade;
- duck the music while a reason is held.

## Open it

1. Install the package from git. A registry or tarball install carries no sample, so copy this folder from the
   repository instead.
2. In **Window > Package Manager**, select **DxAudio** and open the **Samples** tab.
3. Import **Basic usage**. Unity copies it to `Assets/Samples/DxAudio/<version>/BasicUsage`.
4. Open `BasicUsage.unity` and press Play.
5. Use the buttons in the top-left corner:

| Button       | Calls                                       |
| ------------ | ------------------------------------------- |
| Play cue     | `SfxPlayer.Play(cue)`                       |
| Start loop   | `SfxPlayer.TryPlayLooping(cue, out handle)` |
| Stop loop    | `SfxPlayer.StopLoop(handle)`                |
| Duck music   | `MusicPlayer.Duck(duck, holder)`            |
| Release duck | `MusicPlayer.Release(lease)`                |

The scene holds a camera with an `AudioListener`, an `Audio` GameObject with the **Sfx Player**, **Music Player** and
**Audio Diagnostics** components, and a `Demo` GameObject with `BasicUsageDemo`.

## How the sample stays small

`BasicUsageDemo` builds its clips, cues and libraries in code. A game authors the same assets with **Create > DxAudio >
Sfx Cue**, **Music Track** and **Music Library**, then assigns them in the inspector. See
[Documentation~/quickstart.md](../../Documentation~/quickstart.md) for that flow.

## Next

- [Cues](../../Documentation~/cues.md)
- [Music](../../Documentation~/music.md)
- [Ducks](../../Documentation~/ducks.md)
- [Troubleshooting](../../Documentation~/troubleshooting.md)
