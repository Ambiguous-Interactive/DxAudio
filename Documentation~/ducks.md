# Ducks

A duck lowers and filters the music while it is held: for dialogue, a menu, a cutscene. Ducks compose, so several
systems duck and release without knowing about each other.

## Make a duck

Choose **Create > Wallstop Studios > DxAudio > Music Duck** and set:

| Field                       | Does                                                         |
| --------------------------- | ------------------------------------------------------------ |
| **Holders**                 | `One` or `Many`. See below.                                  |
| **Volume Scale**            | Multiplier on the music gain while held.                     |
| **Low Pass Cutoff**         | Lower muffles more. 22000 leaves the low-pass doing nothing. |
| **High Pass Cutoff**        | Higher thins more. 10 leaves the high-pass doing nothing.    |
| **Apply / Release Seconds** | How long the change takes, with an ease curve for each.      |

## Hold it

```csharp
MusicDuckLease lease = _music.Duck(_dialogueDuck, holder: this);
// ...
_music.Release(lease);
```

`Duck` returns a lease. `Release` releases that lease. `Unduck` releases a duck by asset and holder. A lease that was
already released, or whose duck is gone, releases nothing.

## One or Many

- **One** holds a state that is on or off, such as a menu. Asserting it again changes nothing, and one release ends it
  for every holder.
- **Many** collects one claim per holder, such as two speakers. The music stays down until the last holder releases. A
  holder destroyed without releasing is released on the next frame.

## Composing

Every live duck contributes. The result is the most restrictive volume and the most restrictive of each filter cutoff,
so a dialogue duck and a menu duck together sound like both. A released duck leaves no contribution behind.

To check the result, read `MusicPlayer.TargetAttenuation` for the composed level and `Attenuation` for the level applied
this frame. Both report volume, low-pass and high-pass.

```csharp
MusicAttenuation target = _music.TargetAttenuation;
Debug.Log($"music at {target.volumeScale:0.00}, low-pass {target.lowPassCutoffHz:0} Hz");
```

`DescribeDucks()` returns a readable list of every live duck and its holder. The **Music Duck** inspector also shows the
attenuation a chosen set of ducks composes to.

## Reasons for code

A duck asset is a reason a designer can see. Code that holds the music for its own reason can use a plain object:
`MusicDuckReasonTable<TKey>` hands one reason per key, and its constructor takes a `Func<TKey, MusicDuckHolders>` that
says which holders each key allows. An enum value or a string works as a key. The stack keys a hold by the reason's
reference, so use the table instead of creating a new reason on every call.

## Trouble

If the music stays quiet, call `DescribeDucks()`. A `Many` duck still lists its holders until the last one releases. See
[troubleshooting](troubleshooting.md).

## Next

[Troubleshooting](troubleshooting.md).
