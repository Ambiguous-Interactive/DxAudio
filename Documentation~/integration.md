---
doc_type: how-to
---

# DxMessaging integration

DxAudio works without DxMessaging. Install DxMessaging to enable the adapter assembly through its Unity version define.
The players also work with UnityEvents and direct method calls.

With com.wallstop-studios.dxmessaging installed, add **SfxPlayerMessaging** beside an **SfxPlayer**. The adapter turns
messages into player calls and the player's drops into facts, so a game sends a request instead of keeping a reference
to the player.

A loop is a held state, so the player answers with the id that ends it. Register for that answer before sending the
request. This example assumes one caller for the `hum` cue on this player. The message identifies the cue by key.
Import `DxMessaging.Core.Extensions` for the emit methods.

<!-- snippet: integration statements MessageRegistrationToken _registration, GameObject sfxPlayerObject, Action<SfxCueDroppedMessage> HandleDropped, Action<long> SaveLoopId -->

```csharp
_registration.RegisterGameObjectBroadcast<SfxLoopStartedMessage>(sfxPlayerObject, HandleLoopStarted);
_registration.RegisterGameObjectBroadcast<SfxCueDroppedMessage>(sfxPlayerObject, HandleDropped);

// Targeted to the GameObject that carries the player and its adapter.
SfxPlayRequest play = new("hit");
play.EmitGameObjectTargeted(sfxPlayerObject);

SfxPlayRequest placed = new("hit", transform.position);
placed.EmitGameObjectTargeted(sfxPlayerObject);

SfxLoopRequest loop = new("hum");
loop.EmitGameObjectTargeted(sfxPlayerObject);

void HandleLoopStarted(SfxLoopStartedMessage message)
{
    if (message.key == "hum")
    {
        SaveLoopId(message.loopId);
    }
}
```

When the loop should end, send a stop request with the saved id.

<!-- snippet: integration statements GameObject sfxPlayerObject, long loopId -->

```csharp
SfxLoopStopRequest stop = new(loopId);
stop.EmitGameObjectTargeted(sfxPlayerObject);
```

See [SFX messages](reference.md#sfx-messages) for the other requests and reports.

The adapter mints each loop id and ends every loop it started when it is disabled or destroyed, so a stale id stops
nothing. A request the player's own library cannot answer is reported once, as `SfxCueDroppedMessage`, rather than
twice.

Add **MusicPlayerMessaging** beside a **MusicPlayer** for music and pause.
This example uses one caller for the dialogue duck on this player.

<!-- snippet: integration statements MessageRegistrationToken _registration, GameObject musicPlayerObject, Action<MusicTrackChangedMessage> HandleTrack, MusicDuck _dialogueDuck, GameObject speaker, Action<long> SaveDuckId -->

```csharp
_registration.RegisterGameObjectBroadcast<MusicTrackChangedMessage>(musicPlayerObject, HandleTrack);
_registration.RegisterGameObjectBroadcast<MusicDuckStartedMessage>(musicPlayerObject, HandleDuck);

MusicPlayContextRequest music = new("town");
music.EmitUntargeted();

MusicDuckRequest duck = new(_dialogueDuck, speaker);
duck.EmitGameObjectTargeted(musicPlayerObject);

AudioPauseRequest pause = new(true);
pause.EmitUntargeted();

void HandleDuck(MusicDuckStartedMessage message)
{
    if (message.duck == _dialogueDuck)
    {
        SaveDuckId(message.duckId);
    }
}
```

When dialogue ends, send an unduck request with the saved id.

<!-- snippet: integration statements GameObject musicPlayerObject, long duckId -->

```csharp
MusicUnduckRequest unduck = new(duckId);
unduck.EmitGameObjectTargeted(musicPlayerObject);
```

See [music messages](reference.md#music-messages) for the full list.

A duck with `MusicDuckHolders.Many` and a holder is released when that holder is destroyed, as with `MusicPlayer.Duck`.
A `MusicDuckHolders.One` duck is one state for every holder, so it ignores the holder and waits for its id. Destroying
the adapter releases every duck it took.

## Drive ducks from your own messages

Register handlers that call the music player.

<!-- snippet: integration statements MessageRegistrationToken _registration, MusicPlayer _music, MusicDuck _dialogueDuck, MusicDuckLease _lease -->

```csharp
_registration.RegisterUntargeted<DialogueStarted>(m => _lease = _music.Duck(_dialogueDuck, m.speaker));
_registration.RegisterUntargeted<DialogueEnded>(m => _music.Unduck(_dialogueDuck, m.speaker));
```

_Snippet compiled in CI._
