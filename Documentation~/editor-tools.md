---
doc_type: how-to
---

# Editor tools

## Authoring

Open **Tools > Wallstop Studios > DxAudio > Authoring** to browse and edit audio assets. Open a scene mix bus from its Inspector.

- **DSP graphs.** Drag from an output port to connect nodes, or drop on empty space to choose a new node. Open a wire's
  context menu to insert a node. Use **Find Node** or select a validation finding to jump to the affected route.
  **Arrange Nodes** saves node positions with the asset.
- **DSP chains.** Reorder effects in signal order. Open **Chain Effects** on a graph node to tune its chain during audition. Effect edits apply to every chain that uses that effect.
- **DSP audition.** Choose a clip or use the test tone. Press **Play**, then toggle **Dry** to compare at the same monitor
  level. Valid routing and effect edits take effect during playback. An invalid draft keeps the last playable version
  audible. If Unity sends no Edit Mode audio, enter Play Mode and press **Play** again.
- **Libraries.** Search members by name, cue key, or music context. Open a member to edit it and review validation findings.

- **Cue inspector.** Each take shows where playback starts after its clip's lead-in, with an **Audition** button that
  plays from that point. The same panel reports a cue that no take can play.
- **Duck inspector.** Add the ducks that can hold the music at the same time to see the attenuation they compose to.
- **Library validation.** **Tools > Wallstop Studios > DxAudio > Validate Libraries** checks every SFX and music library for missing clips,
  duplicate keys or contexts, and tracks no context reaches.

## Set up a DSP bus

To use the same chain on each voice, assign **Bus Chain** on the player or set it in code.

<!-- snippet: unity statements SfxPlayer _sfx, MusicPlayer _music, DspChainAsset sfxBus, DspChainAsset musicBus -->

```csharp
_sfx.BusChain = sfxBus;
_music.BusChain = musicBus;
```

To process the summed output, add **Audio Mix Bus** to the GameObject with the **Audio Listener** and assign the chain.

<!-- snippet: unity statements GameObject _listenerObject, DspChainAsset masterChain -->

```csharp
_listenerObject.AddComponent<AudioMixBus>().Chain = masterChain;
```

See [bus behavior](effects.md#a-player-wide-bus) before choosing where to place a limiter.

## Play Mode diagnostics

Open **Tools > Wallstop Studios > DxAudio > Mix Monitor** in Play Mode. Select an SFX Player, a Music Player, or both from the scene.
The window shows each drop reason and its Problems rule ID. Counts cover the selected player's lifetime.
Click **Pause** to hold the current counts. **Resume** refreshes them, including drops that occurred while paused.
Click an occupied SFX voice bar to pause capture and inspect its recorded clip, playback position, and priority.
Move the **Sample** slider to inspect the same voice at another point in the retained history.
Click **Silence** to hide a reason's count and move it into the silenced total. Throttled drops start silenced.
The **Music** section shows each deck's fade and the applied duck gain on the same ruler as the SFX timeline.
Pause and move the **Sample** slider to read the deck's track and fade and the duck gain at a point in the capture.
Each player strip lists its configured **Bus Inserts** in signal order. Hover over the list to read it in full.
The list stays live when you pause the timeline.
To meter the live master, select the Audio Mix Bus on the active Audio Listener. The L/R bars show sample peaks in dBFS.
**True peak** shows the highest estimated peak across all listener channels in dBTP, including peaks between samples.
Each meter holds its peak for one second and stays live when you pause the timeline.
**Audio gap** appears if the editor falls behind and misses audio for the true-peak estimate.

Add an **Audio Diagnostics** component (**Add Component > Wallstop Studios > DxAudio > Audio Diagnostics**) and assign the players. Tick
**Enabled** in Play mode, or set `diagnostics.Enabled = true`. The overlay lists every busy voice with its priority and
loop lease, every duck and who holds it, and how many cues were dropped by reason.

Sampling runs only while **Enabled** is set. With it off, the component allocates nothing per frame; drawing costs apply
only while the overlay is open. `SfxPlayer.DropCounts` and `SfxPlayer.TryGetVoiceState` expose the same data for a
game's own debug UI.

See [Troubleshooting](troubleshooting.md) for steps to diagnose silent or dropped sounds.

## Author a chain

1. Create one asset per effect with **Create > Wallstop Studios > DxAudio > DSP > Limiter** (or Compressor, Distortion, Bit Crusher, Pitch
   Shifter, Filter, Delay, Chorus, Tremolo, Phaser, Reverb, Noise Gate, Stereo Width).
2. Create the list with **Create > Wallstop Studios > DxAudio > DSP > Chain** and add the effects in the order the signal should meet
   them. Order is the sound: distortion before a compressor is driven and then controlled, and the reverse squashes the
   quiet part before the drive lifts it again.
3. Assign the chain to a cue or a track. Both inspectors list the stages in order.

Put a limiter last if a compressor's makeup gain can pass full scale.

## Create a node graph

1. Create the graph with **Create > Wallstop Studios > DxAudio > DSP > Graph**.
2. Add one node per chain the graph runs. Give each node a unique name, then assign its chain. A node with no chain is a
   junction that passes its input on untouched, which is how a split or a join with no shaping of its own is authored.
3. Add one edge per connection. Name the node the signal leaves and the node it enters.
4. Set **Input Node** and **Output Node** to the names of the nodes that receive the voice's block and leave the graph.
5. Assign the graph to a cue or a track. The graph's **Signal Path** section lists each node with the delay it adds,
   every edge, and the graph's own delay.

A graph can also be built in code. Pass the session's sample rate, such as `AudioSettings.outputSampleRate` in Unity.

<!-- snippet: core statements DspChain roomChain, DspChain dryChain, int sampleRate, float[] buffer -->

```csharp
DspGraph graph = new();
_ = graph.TryAddNode(new DspChain(), out int input);
_ = graph.TryAddNode(roomChain, out int room);
_ = graph.TryAddNode(dryChain, out int dry);
_ = graph.TryAddNode(new DspChain(), out int output);
_ = graph.TryAddEdge(input, room);
_ = graph.TryAddEdge(input, dry);
_ = graph.TryAddEdge(room, output);
_ = graph.TryAddEdge(dry, output);
_ = graph.TryPrepare(sampleRate, 1024, 2, input, output);
graph.Process(buffer, 0, buffer.Length, 2);
```

## Draw an EQ curve

To fit a drawn response, select a **DSP Chain** asset and open **Drawn EQ** in the Inspector.

1. Select the output sample rate and a band limit. Drag across the plot in either direction or load one of the
   example targets.
2. Select **Fit target**. Compare the combined Bell response with the target on the 20 Hz to 20 kHz logarithmic
   axis. The Inspector reports RMS error and the largest sampled error in dB. Compare different band limits and
   use the one that meets your target.
3. Select **Save new EQ chain**. The asset saves the target, preview rate, and band limit with its generated Bell stages.
4. Reopen the asset to compare the target with **Current generated Bells**. Change the target or settings and select
   **Fit target**. Select **Apply fit to this chain** to save the changes. Unity Undo restores the previous settings
   and Bell parameters in one step.

## Build a Bell equalizer

You can also create one **Filter** asset per band and set its mode to **Bell**. Put the filters in a **Chain** so the signal passes
through all bands in order. Assign the chain to a cue or track for local shaping, or use it on an [Audio Mix Bus](effects.md#a-mix-bus)
for the listener mix. Inside a graph, put the whole chain in one node on the signal path.

These starting points use **Quality** 1 on every band. Set **Peak Decibels** to 0 for a flat start and adjust the bands
by ear. The same setup works with other band counts or center frequencies.

| Bands | Center frequencies (Hz)                                           |
| ----- | ----------------------------------------------------------------- |
| 7     | 63, 125, 250, 500, 1,000, 2,000, 4,000                            |
| 11    | 31, 63, 125, 250, 500, 1,000, 2,000, 4,000, 8,000, 12,000, 16,000 |

See [EQ behavior and measurements](effects.md#multiband-equalizer).

_Snippet compiled in CI._
