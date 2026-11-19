# Effects

A cue or a music track can name a **DSP chain**: a list of effects that shape the sound on its way out. A chain is an
asset, like a cue or a track, so a designer changes the sound without a code change.

## What an effect is for

| Effect        | Use it to                                                       |
| ------------- | --------------------------------------------------------------- |
| Limiter       | Hold the loudest sample under a ceiling, with a short lookahead |
| Compressor    | Turn the loud part down and leave the quiet part where it is    |
| Distortion    | Drive a sound into a shaper, then tame the top with a tone knob |
| Bit Crusher   | Drop a sound to fewer bits and a lower sample rate, for grit    |
| Pitch Shifter | Move a sound up or down in pitch without changing its length    |
| Filter        | Muffle, thin, isolate or lift one band around a corner          |

## Author a chain

1. Create one asset per effect with **Create > DxAudio > DSP > Limiter** (or Compressor, Distortion, Bit Crusher, Pitch
   Shifter, Filter).
2. Create the list with **Create > DxAudio > DSP > Chain** and add the effects in the order the signal should meet
   them. Order is the sound: distortion before a compressor is driven and then controlled, and the reverse squashes the
   quiet part before the drive lifts it again.
3. Assign the chain to a cue or a track. Both inspectors list the stages in order.

Put a limiter last if a compressor's makeup gain can pass full scale.

## A player-wide bus

Each player has one **Bus Chain**. It runs after every voice's own chain, so it is one place to shape the whole sound.

```csharp
_sfx.BusChain = sfxBus;
_music.BusChain = musicBus;
```

Unity gives a script no way to sum one source into another, so the bus runs once per voice rather than on the mixed
output. Every bus effect, a limiter and a distortion included, acts on one voice at a time: two voices that each fit
under a ceiling can still sum above it. A bus is a shared shaping stage, not an output limiter. Use a mix bus for that.

## A mix bus

Unity sums every source before it calls an audio filter on the object that carries the `AudioListener`. An **Audio Mix
Bus** on that object therefore shapes the whole mix, including sources this package does not play:

```csharp
_listenerObject.AddComponent<AudioMixBus>().Chain = masterChain;
```

- A limiter here holds a ceiling over the sum. Two voices that each leave their own bus at -6 dB leave this one at
  -6 dB as well, instead of summing above it.
- It runs once per block, not once per voice, so its cost does not grow with the pool.
- It adds no buffer of its own. `LatencySamples` reports only what its chain asks for: a limiter's lookahead, a pitch
  shifter's half window.
- The component belongs on the listener's object. Anywhere else it receives no audio, and its inspector says so.
- It shapes one sum, not a routing tree. Separate Master, Music and SFX groups with their own effects stay with
  `AudioMixer` groups through the libraries' **Mixer Group** fields.

## What a voice keeps

Every voice builds its own copy of a chain, because an effect keeps a delay line or an envelope that two voices must
not share. A voice clears both its cue chain and the bus when it starts a play, so a pooled voice never inherits the
last sound's gain or delay line.

A voice reads an asset once, the first time it meets it, and keeps those settings until it is destroyed: an edit
reaches the voices that have not met the asset yet. Chain assets a library lists are prepared with the voice pool, so
the first play of them allocates nothing. A cue played by reference can be prepared ahead with `_sfx.Warm(cue)`.

## Pitch shifter

The pitch shifter reads one delay line with two heads that travel at the rate the pitch asks for, crossfading with a
triangular window so the head that jumps back is always silent. It is a granular shift, not a phase vocoder: no library
or FFT is needed and a transient is not smeared across the window, at the cost of some warble on a pure tone; a sample
that falls between two read positions is skipped.

- **Semitones** moves the pitch from -24 to 24. At zero the sweep parks where one head is silent, so the sound passes
  through unchanged, only delayed. Turning the knob back to zero crossfades into that parked read over ten milliseconds,
  so the change does not click. On a sustained high tone that crossfade can dip for about two milliseconds, because the
  two reads are at different delays; a note or a transient hides it.
- **Window** is how long each head sweeps. The delay is half the rounded window plus one frame: a 50 ms window at 48 kHz
  delays 1,201 samples, about 25 ms. A shorter window lowers the delay and adds warble; a longer one smooths the tone. Set
  it before playback: the delay line is sized when the chain is prepared, not when the field changes.

## Filter

The filter shapes one band around a corner. Every mode reads the same two-integrator core, so a mode change swaps the
output the stage keeps and nothing else:

| Mode      | What leaves the stage                                      | A game use                         |
| --------- | ---------------------------------------------------------- | ---------------------------------- |
| Low Pass  | Below the corner, rolling off above it                     | Muffled, underwater, behind a wall |
| High Pass | Above the corner, rolling off below it                     | Thin, distant, through a wall      |
| Band Pass | A window around the corner                                 | Radio, telephone, a small speaker  |
| Notch     | Everything except a window around the corner               | Remove a hum or a whistle          |
| Peak      | The whole sound, with the band lifted by **Peak Decibels** | Add presence or air                |

- **Frequency** is the corner, from 10 Hz up. The prepared rate caps it at 45% of the sample rate, so a corner above
  Nyquist is a corner just under it rather than an unstable coefficient.
- **Quality** is how narrow the band is. 0.71 is flat at the corner; higher narrows it and lifts the corner for a low
  pass or a band pass, and deepens a notch.
- **Peak Decibels** moves only the Peak band, from -24 to 24 dB. Zero decibels passes the block through sample for
  sample.
- A knob moved while the sound plays is reached over ten milliseconds, so a sweep does not click. Set the corner before
  playback when the exact first block matters.

Measured at 48 kHz with a 1 kHz corner: a low pass leaves a 100 Hz tone at 0.0 dB and a 8 kHz tone at -37.8 dB, a high
pass does the reverse, a band pass with quality 2 leaves its centre at 0.0 dB and a tone an octave away at -10 dB, and a
notch at quality 2 leaves its centre at -127 dB.

## A node graph

A chain runs its stages in series. A **graph** runs chains side by side: a **DSP Graph** asset holds named nodes, each
with a chain, and an edge carries one node's block into a later node. A node with several outgoing edges feeds every
one, which is the fan-out, and a node with several incoming edges sums them, which is the fan-in.

1. Create the graph with **Create > DxAudio > DSP > Graph**.
2. Add one node per chain the graph runs. Give each node a unique name, then assign its chain. A node with no chain is a
   junction that passes its input on untouched, which is how a split or a join with no shaping of its own is authored.
3. Add one edge per connection. Name the node the signal leaves and the node it enters.
4. Set **Input Node** and **Output Node** to the names of the nodes that receive the voice's block and leave the graph.
5. Assign the graph to a cue or a track. The graph's **Signal Path** section lists each node with the delay it adds,
   every edge, and the graph's own delay.

The graph runs after the cue's or track's chain and before the player's bus. A node's name is its identity because an
edge names it, so two nodes may not share one, and an edge to a name no node holds is refused rather than guessed at.
The cue inspector and the library check report a graph the runtime cannot build, which would otherwise pass the sound
through unshaped.

A graph can also be built in code, for a path a game assembles at run time:

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
_ = graph.TryPrepare(AudioSettings.outputSampleRate, 1024, 2, input, output);
graph.Process(buffer, 0, buffer.Length, 2);
```

- A branch that reports less delay is waited for: the graph delays that edge by the difference its chains report, so a
  pitch shifter in one branch and a limiter in another arrive together and sum without a comb. Read the graph's own
  delay with `LatencySamples`; it is the longest path, and `TryGetNodeLatency` reports one branch.
- `TryPrepare` refuses a cycle, a node the input cannot reach or one that cannot reach the output, a layout it cannot
  run and a graph over its memory budget. It answers `false` and reserves nothing, so a bad graph never throws.
- A graph holds up to 16 nodes and 32 edges, and `PreparedBytes` reports what it reserved. `Process` allocates nothing
  once prepared: build the graph and prepare it before the first block.

## Limits

- A chain runs on the audio thread. Effects allocate when they are prepared, never while they run.
- A limiter delays the signal by its lookahead, and a pitch shifter by half its window. Read the delay with
  `AudioFilterHost.LatencySamples`; a chain does not compensate. Put the two in separate branches of a graph and it
  aligns them for you.
- A graph asset is a recipe like a chain: a voice builds its own copy, one per block shape, so two voices never share a
  branch buffer. A cue or a track names at most one chain and one graph.
- A block whose shape no prepared chain matches passes through and counts in `AudioFilterHost.SkippedBlocks`, so a
  silent bypass is visible rather than guessed at.
