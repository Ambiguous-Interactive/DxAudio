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

## Play Mode diagnostics

Add an **Audio Diagnostics** component (**Add Component > Wallstop Studios > DxAudio > Audio Diagnostics**) and assign the players. Tick
**Enabled** in Play mode, or set `diagnostics.Enabled = true`. The overlay lists every busy voice with its priority and
loop lease, every duck and who holds it, and how many cues were dropped by reason.

Sampling runs only while **Enabled** is set. With it off, the component allocates nothing per frame; drawing costs apply
only while the overlay is open. `SfxPlayer.DropCounts` and `SfxPlayer.TryGetVoiceState` expose the same data for a
game's own debug UI.

See [Troubleshooting](troubleshooting.md) for steps to diagnose silent or dropped sounds.
