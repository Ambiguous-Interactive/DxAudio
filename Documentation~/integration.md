# DxMessaging integration

DxAudio works without DxMessaging. Install DxMessaging to enable the adapter assembly through its Unity version define.
The players also work with UnityEvents and direct method calls.

With com.wallstop-studios.dxmessaging installed, add **SfxPlayerMessaging** beside an **SfxPlayer**. The adapter turns
messages into player calls and the player's drops into facts, so a game sends a request instead of keeping a reference
to the player.

A loop is a held state, so the player answers with the id that ends it. Register for that answer before sending the
request. Import `DxMessaging.Core.Extensions` for the emit methods.

<!-- snippet: integration statements MessageRegistrationToken _registration, GameObject sfxPlayerObject, Action<SfxCueDroppedMessage> HandleDropped -->

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

<!-- snippet: integration statements MessageRegistrationToken _registration, GameObject musicPlayerObject, Action<MusicTrackChangedMessage> HandleTrack, MusicDuck _dialogueDuck, GameObject speaker -->

```csharp
_registration.RegisterGameObjectBroadcast<MusicTrackChangedMessage>(musicPlayerObject, HandleTrack);
_registration.RegisterGameObjectBroadcast<MusicDuckStartedMessage>(musicPlayerObject, HandleDuck);

MusicPlayContextRequest music = new("town");
music.EmitUntargeted();

MusicDuckRequest duck = new(_dialogueDuck, speaker);
duck.EmitGameObjectTargeted(musicPlayerObject);

// The answer carries the id that releases this hold.
void HandleDuck(MusicDuckStartedMessage message)
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

## Drive ducks from your own messages

Register handlers that call the music player.

<!-- snippet: integration statements MessageRegistrationToken _registration, MusicPlayer _music, MusicDuck _dialogueDuck, MusicDuckLease _lease -->

```csharp
_registration.RegisterUntargeted<DialogueStarted>(m => _lease = _music.Duck(_dialogueDuck, m.speaker));
_registration.RegisterUntargeted<DialogueEnded>(m => _music.Unduck(_dialogueDuck, m.speaker));
```

_Snippet compiled in CI._
