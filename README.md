# DxAudio

Data-driven sound effects and music for Unity, with no dependencies.

- **Cues and tracks are assets.** An `SfxCue` holds interchangeable takes, volume, pitch jitter, a rate limit, a
  priority and 3D falloff. A `MusicTrack` holds a clip, its trim and where it starts. Libraries look them up by key, or
  you play them by reference.
- **A pooled voice player.** `SfxPlayer` plays cues on a fixed pool of voices. It never allocates per play, never
  repeats a take twice in a row, rate-limits each cue on its own, and lets important cues take a voice from less
  important ones when the pool is full. Looping cues hold their voice until their handle stops them.
- **Music per context.** `MusicPlayer` crossfades between contexts (a screen, a level, an area), keeps playing when the
  same music is asked for again, and fades music back in from where it was if you return while it is still fading out.
  Overrides play other music until their lease is released.
- **Ducks that compose.** A `MusicDuck` asset (menu, dialogue, cutscene) lowers and filters the music while it is held.
  Every live duck composes into the most restrictive volume and filters, so systems duck and release without knowing
  about each other. A holder destroyed without releasing is released for it.
- **Your game wires the hooks.** Sound on/off and music on/off are one property each. Events are plain method calls, so
  DxMessaging messages, UnityEvents or your own code can drive them.

Extracted from [Isho Boy](https://github.com/Ambiguous-Interactive)'s audio system.

## Documentation

The manual lives in [Documentation~/index.md](Documentation~/index.md): install, quickstart, cues, music, ducks and
troubleshooting. `Samples~/BasicUsage` holds one scene that plays a cue, holds a loop, starts music and ducks it.

## Install

Unity 2021.3 or newer.

**From npm.** Add the scoped registry and the package to `Packages/manifest.json`. Package Manager then lists new
versions under **My Registries**:

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

The `com.wallstop-studios` scope also covers DxMessaging, Unity Helpers and the other Wallstop Studios packages on npm.
If your manifest already has a scoped registry for `https://registry.npmjs.org`, add the scope to it instead.

**From OpenUPM.** With the [OpenUPM CLI](https://openupm.com/docs/getting-started-cli.html), run
`openupm add com.wallstop-studios.dxaudio`. Without it, add a scoped registry for `https://package.openupm.com` with the
scope `com.wallstop-studios.dxaudio`.

**From git.** Open **Window > Package Manager**, choose **+ > Add package from git URL**, and enter:

```text
https://github.com/Ambiguous-Interactive/DxAudio.git#v0.3.0
```

**From a tarball.** Download `com.wallstop-studios.dxaudio-<version>.tgz` from a
[release](https://github.com/Ambiguous-Interactive/DxAudio/releases) and choose **+ > Add package from tarball**.

### Dependencies

DxAudio needs no other package. Its assemblies reference only Unity's, so npm, OpenUPM, a git URL and a release tarball
install the same files and fetch nothing else. DxMessaging, UnityEvents or your own code drive the players with plain
method calls (see [Settings and events](#settings-and-events)).

One integration is optional. `Runtime/Integrations/DxMessaging` compiles only when the project also has
com.wallstop-studios.dxmessaging installed, through a version define and a define constraint on its assembly. Without
the package that assembly is skipped entirely, so a DxMessaging-free project sees no missing reference and compiles
exactly as before. Pull-request and main CI run Unity 6 in both states, for same-repository pull requests that are not
from Dependabot; fork and Dependabot pull requests skip the Unity lanes. The same job matrix runs nightly on Unity
2021.3 in both states.

If a later version needs another package, `package.json` lists it at an exact version. Unity then resolves it from the
registries in your manifest for every install path. CI fails when an assembly references a package that `package.json`
does not list.

## Sound effects

1. Create cues with **Create > DxAudio > Sfx Cue**. Add one or more takes; each take has its own start offset to skip a
   lead-in.
2. Create a library with **Create > DxAudio > Sfx Library**, list the cues, and set the voice count.
3. Add an **Sfx Player** component and assign the library.

```csharp
using WallstopStudios.DxAudio;

public sealed class Door : MonoBehaviour
{
    [SerializeField] private SfxPlayer _sfx;
    [SerializeField] private SfxCue _open;

    public void Open()
    {
        _sfx.Play(_open);                       // flat
        _sfx.Play(_open, transform.position);   // placed, if the cue has a spatial blend
        _sfx.Play("door/creak");                // by key, through the library
    }
}
```

A looping cue stands for a state, so whoever starts it stops it:

```csharp
if (_sfx.TryPlayLooping(_hum, out SfxLoopHandle hum))
{
    _humHandle = hum;
}

_sfx.StopLoop(_humHandle); // a stale handle stops nothing
```

`SfxPlayer.CueDropped` reports every cue that did not play, and why (`Throttled`, `NoVoice`, `LoopLimit`, ...).

### How a voice is chosen

| Situation                                | Result                                                                                |
| ---------------------------------------- | ------------------------------------------------------------------------------------- |
| A voice is idle                          | The cue takes it, searching round-robin so cues spread across the pool.               |
| Every voice is busy                      | The cue takes the least important voice of **strictly lower** priority, oldest first. |
| Only equal or higher priority is playing | The cue is dropped. Cutting off an audible sound is worse than one that never starts. |
| A voice holds a loop                     | It is never taken.                                                                    |

### Effects

A cue or a music track can name a **DSP chain** (**Create > DxAudio > DSP > Chain**), a list of limiter, compressor,
distortion, bit crusher and pitch shifter stages in the order the signal meets them. Every pooled voice builds its own
copy the first time it meets a chain, from the settings the asset holds then, so an edit reaches the voices that have
not met it yet. A voice clears its cue chain, its graph and the bus when it starts a play, so it never inherits the
last sound's delay line.

A cue or a track may also name a **DSP Graph** (**Create > DxAudio > DSP > Graph**), which runs chains side by side: a
node holds a chain, an edge carries one node's signal into a later one, and a node that takes several edges sums them.
The graph delays the shorter branch so every path reaches its output together. It runs after the voice's chain and
before the bus, and the graph inspector lists each node with the delay it adds.

```csharp
_sfx.BusChain = sfxBusChain;   // runs after each voice's own chain
_music.BusChain = musicBusChain;
_sfx.Warm(cue);                // prepares a cue the library does not list, before its first play
```

A bus runs on every voice rather than on the mixed output, because Unity gives a script no way to sum one source into
another. Every bus effect, a limiter and a distortion included, therefore acts on one voice at a time: two voices that
each fit under a ceiling can still sum above it, so a bus is a shared shaping stage and not an output limiter. The
chain, cue and track inspectors show each stage with its live gain reduction while audio plays.

## Music

1. Create tracks with **Create > DxAudio > Music Track** and give each a context, such as `title` or `level-1`.
2. Create a library with **Create > DxAudio > Music Library**, list the tracks, and set the fades.
3. Add a **Music Player** component and assign the library.

```csharp
_music.PlayContext("level-1");     // crossfades; the same music asked for again keeps playing
_music.Play(track);                // or by reference
_music.Stop();                     // fades out

using (_music.Override(bossTrack)) // plays over the context, then hands back
{
    await BossFight();
}
```

### Ducking

Create a duck with **Create > DxAudio > Music Duck**. Set its volume, low-pass and high-pass, its apply and release
times, and its holders:

- **One** for a state that is on or off, such as a menu. Asserting it again changes nothing, and one release ends it.
- **Many** for a claim several things make at once, such as two speakers. The music stays down until the last one
  releases.

```csharp
MusicDuckLease lease = _music.Duck(_dialogueDuck, holder: this);
// ...
_music.Release(lease);
```

A reason that only code raises can be a plain object. `MusicDuckReasonTable<TKey>` hands one per key, an enum value or a
string, because the stack keys a hold by the reason's reference.

A `Many` duck whose holder is destroyed without releasing is released on the next frame. `MusicPlayer.DescribeDucks()`
lists every live duck and its holder.

### Mixer groups

A music library, an SFX library, and a single cue can each name an `AudioMixerGroup`, so a game keeps its own Master,
Music, and SFX groups and their volume sliders. Ducking does not need snapshots, and several reasons hold the music down
at once. The composed level goes to each music voice, and `MusicPlayer.Attenuation` reports the level applied this frame
while `TargetAttenuation` reports the level the live ducks compose to.

## Clip lifetime

The players never own a clip. A game that loads and unloads clips itself, with Addressables or `Resources`, decides when
a clip goes. These are the rules that make a release safe:

| Moment                                       | What happens                                                                                        |
| -------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Released before its cue plays                | The cue has nothing to play: `Play` returns false and `CueDropped` reports `NotPlayable`.           |
| Released while an SFX loop plays it          | The hold ends within a frame and the handle then stops nothing.                                     |
| Released while an SFX one-shot plays         | The voice makes no sound and is not busy, so the next `Play` stops it while it reads the pool.      |
| Released while its music plays               | The music stops. Asking for its context again returns true and starts it once the track has a clip. |
| Its data unloaded while an SFX loop plays it | Same as a release: the hold ends within a frame and the handle then stops nothing.                  |
| Its data unloaded while its music plays      | `IsPlaying` reads false, and the next frame stops the voice so the music after it starts cleanly.   |

Unity clears a source's clip when that clip's asset is destroyed, and keeps the clip assigned when its audio data is
unloaded, by `AudioClip.UnloadAudioData`, `Resources.UnloadAsset`, or an Addressables release. In both cases the source
still reports playback with no sound. The players check the clip and its load state instead of trusting that report, so
a voice never stays stranded and a music context never sticks as silent. `Unloaded` and `Failed` count as gone, and the
check reads a voice that reports playback, so a streaming clip that reports `Loading` or `Partially` as it starts is not
mistaken for a released one.

Releasing early is safe but cuts the sound short. `Stop` and `StopLoop` end a sound on purpose, so let a music fade
finish before unloading its clip if you want to hear it. Load a clip again with `AudioClip.LoadAudioData`, or start it
again and Unity loads it as it plays.

## Authoring

- **Cue inspector.** Each take shows where playback starts after its clip's lead-in, with an **Audition** button that
  plays from that point. The same panel reports a cue that no take can play.
- **Duck inspector.** Add the ducks that can hold the music at the same time to see the attenuation they compose to.
- **Library validation.** **Tools > DxAudio > Validate Libraries** checks every SFX and music library for missing clips,
  duplicate keys or contexts, and tracks no context reaches.

## Settings and events

```csharp
_sfx.Muted = !settings.SoundOn;
_music.Muted = !settings.MusicOn;
```

Muting keeps every voice's state, so loops and music resume in time when unmuted. To drive the players from DxMessaging,
register handlers that call them:

```csharp
_registration.RegisterUntargeted<DialogueStarted>(m => _lease = _music.Duck(_dialogueDuck, m.speaker));
_registration.RegisterUntargeted<DialogueEnded>(m => _music.Unduck(_dialogueDuck, m.speaker));
```

Pause holds every sounding voice at its playhead and keeps loops, leases and mute state, so resume continues rather than
restarts. A cue or track started during the pause waits at its start. A lower-priority cue cannot replace a paused
one-shot; an equal or higher one may. It is separate from `AudioListener.pause`: set it when the game wants audio held.

```csharp
_sfx.Paused = paused;
_music.Paused = paused;
```

## DxMessaging integration

With com.wallstop-studios.dxmessaging installed, add **SfxPlayerMessaging** beside an **SfxPlayer**. The adapter turns
messages into player calls and the player's drops into facts, so a game sends a request instead of keeping a reference
to the player.

A loop is a held state, so the player answers with the id that ends it. Register for that answer before sending the
request, because a broadcast nobody is listening for is gone:

```csharp
using DxMessaging.Core.Extensions;

_registration.RegisterGameObjectBroadcast<SfxLoopStartedMessage>(sfxPlayerObject, HandleLoopStarted);
_registration.RegisterGameObjectBroadcast<SfxCueDroppedMessage>(sfxPlayerObject, HandleDropped);

// Targeted to the GameObject that carries the player and its adapter.
SfxPlayRequest play = new("hit");
play.EmitGameObjectTargeted(sfxPlayerObject);

SfxPlayRequest placed = new("hit", transform.position);
placed.EmitGameObjectTargeted(sfxPlayerObject);

SfxLoopRequest loop = new("hum");
loop.EmitGameObjectTargeted(sfxPlayerObject);

private void HandleLoopStarted(SfxLoopStartedMessage message)
{
    SfxLoopStopRequest stop = new(message.loopId);
    stop.EmitGameObjectTargeted(sfxPlayerObject);
}
```

| Message                 | Kind       | Does                                        |
| ----------------------- | ---------- | ------------------------------------------- |
| `SfxPlayRequest`        | Targeted   | Plays one cue by key, flat or at a position |
| `SfxLoopRequest`        | Targeted   | Holds a looping cue and answers with its id |
| `SfxLoopStopRequest`    | Targeted   | Ends the loop an id names                   |
| `SfxStopAllRequest`     | Targeted   | Stops every voice, loops included           |
| `SfxMuteRequest`        | Untargeted | Sets every effect player's mute state       |
| `SfxLoopStartedMessage` | Broadcast  | Reports a held loop and its id              |
| `SfxCueDroppedMessage`  | Broadcast  | Reports a cue that did not play, and why    |

The adapter mints each loop id and ends every loop it started when it is disabled or destroyed, so a stale id stops
nothing. A request the player's own library cannot answer is reported once, as `SfxCueDroppedMessage`, rather than
twice.

Add **MusicPlayerMessaging** beside a **MusicPlayer** for music and pause:

```csharp
_registration.RegisterGameObjectBroadcast<MusicTrackChangedMessage>(musicPlayerObject, HandleTrack);
_registration.RegisterGameObjectBroadcast<MusicDuckStartedMessage>(musicPlayerObject, HandleDuck);

MusicPlayContextRequest music = new("town");
music.EmitUntargeted();

MusicDuckRequest duck = new(_dialogueDuck, speaker);
duck.EmitGameObjectTargeted(musicPlayerObject);

// The answer carries the id that releases this hold.
private void HandleDuck(MusicDuckStartedMessage message)
{
    MusicUnduckRequest unduck = new(message.duckId);
    unduck.EmitGameObjectTargeted(musicPlayerObject);
}

AudioPauseRequest pause = new(true);
pause.EmitUntargeted();
```

| Message                    | Kind       | Does                                          |
| -------------------------- | ---------- | --------------------------------------------- |
| `MusicPlayContextRequest`  | Untargeted | Plays the music a context names               |
| `MusicStopRequest`         | Untargeted | Fades the music out                           |
| `MusicDuckRequest`         | Targeted   | Holds the music and answers with its duck id  |
| `MusicUnduckRequest`       | Targeted   | Releases the duck an id names                 |
| `AudioPauseRequest`        | Untargeted | Holds or resumes every effect and music voice |
| `MusicTrackChangedMessage` | Broadcast  | Reports the track that now plays              |
| `MusicStoppedMessage`      | Broadcast  | Reports that the music stopped                |
| `MusicDuckStartedMessage`  | Broadcast  | Reports a held duck and its id                |

A duck with `MusicDuckHolders.Many` and a holder is released when that holder is destroyed, as with `MusicPlayer.Duck`.
A `MusicDuckHolders.One` duck is one state for every holder, so it ignores the holder and waits for its id. Destroying
the adapter releases every duck it took.

## Diagnostics

Add an **Audio Diagnostics** component (**Add Component > DxAudio > Audio Diagnostics**) and assign the players. Tick
**Enabled** in Play mode, or set `diagnostics.Enabled = true`. The overlay lists every busy voice with its priority and
loop lease, every duck and who holds it, and how many cues were dropped by reason.

Sampling runs only while **Enabled** is set. With it off, the component allocates nothing per frame; drawing costs apply
only while the overlay is open. `SfxPlayer.DropCounts` and `SfxPlayer.TryGetVoiceState` expose the same data for a
game's own debug UI.

## The core is engine-free

`Runtime/Core` (the arbiter, rate limit, take selection, duck stack, attenuation, ramps, hand-off and override) uses no
Unity types. Use those types directly to build your own player, and test them on plain .NET:

```bash
dotnet test .net-tests
```

The same tests, plus tests of the assets and players, run in Unity's Test Runner when the package is listed under
`testables` in your project manifest.

## Development

The package repo lives under a Unity project's `Packages/` folder. Open this folder in VS Code and choose **Reopen in
Container** for the full toolchain. Copy `.env.local.example` to `.env.local` and fill the keys you need.

On the host, keep the Unity editor open and start the MCP bridge:

```bash
npm run unity:mcp:host
```

Then, in the container or the host:

```bash
npm run check:gate      # format, lints, harness self-tests, npm payload
npm run unity:tests     # EditMode suite through the local editor
dotnet test .net-tests  # engine-free core on plain .NET
```

See [.devcontainer/README.md](.devcontainer/README.md) for the container lifecycle and
[.llm/context.md](.llm/context.md) for agent rules.

## License

[MIT No Attribution](LICENSE.md).
