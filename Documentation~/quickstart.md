# Quickstart

This page plays a cue and starts music in an empty scene. It takes about five minutes.

## 1. Make a cue

1. Choose **Create > Wallstop Studios > DxAudio > Sfx Cue** and name it `Door`.
2. Give it the key `door`.
3. Add one or more takes. Each take names an `AudioClip`. A take can start past the clip's lead-in with **Start Time
   Seconds**.

## 2. Make a library

1. Choose **Create > Wallstop Studios > DxAudio > Sfx Library** and name it `SoundLibrary`.
2. Set **Voice Count** to `16`. This is how many sounds can play at once.
3. Add the cue to the **Cues** list.

## 3. Add a player

1. Add an empty GameObject named `Audio`.
2. Add the **Sfx Player** component, then assign `SoundLibrary` to **Library**.

## 4. Play a cue

Call the player from your own code. The key comes from the cue asset:

<!-- snippet: unity file -->

```csharp
using UnityEngine;
using WallstopStudios.DxAudio;

public sealed class Door : MonoBehaviour
{
    [SerializeField]
    private SfxPlayer _sfx;

    public void Open()
    {
        _sfx.Play("door");
    }
}
```

`Play` returns `true` when the cue started. For a placed sound, pass a position. The cue must have a **Spatial Blend**
above zero, or it still plays flat:

<!-- snippet: unity statements SfxPlayer _sfx -->

```csharp
_sfx.Play("door", transform.position);
```

## 5. Add music

1. Choose **Create > Wallstop Studios > DxAudio > Music Track**. Set **Context** to `level-1` and assign the clip.
2. Choose **Create > Wallstop Studios > DxAudio > Music Library**, add the track, and set the fade times.
3. Add a **Music Player** component to the `Audio` GameObject and assign the library.

<!-- snippet: unity members -->

```csharp
[SerializeField]
private MusicPlayer _music;

private void Start()
{
    _music.PlayContext("level-1"); // crossfades in
}
```

## 6. Check the result

- Add an **Audio Diagnostics** component beside the players and tick **Enabled** in Play mode. The overlay lists every
  busy voice and every active duck.
- Subscribe to `SfxPlayer.CueDropped` from `OnEnable` while debugging. It reports `Throttled`, `NoVoice`, `NotPlayable`, `LoopLimit` and
  `WrongPlayMethod`.

<!-- snippet: unity statements SfxPlayer _sfx -->

```csharp
_sfx.CueDropped += (cue, reason) => Debug.Log($"{cue} dropped: {reason}");
```

## Next

- [Cues](cues.md): takes, priority, rate limits and 3D falloff.
- [Music](music.md): contexts, overrides and mixer groups.
- [Ducks](ducks.md): lower the music for dialogue.
- [Troubleshooting](troubleshooting.md): when nothing plays.

_Snippet compiled in CI._
