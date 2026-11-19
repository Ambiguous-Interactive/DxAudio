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

## Author a chain

1. Create one asset per effect with **Create > DxAudio > DSP > Limiter** (or Compressor, Distortion, Bit Crusher, Pitch
   Shifter).
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
under a ceiling can still sum above it. A bus is a shared shaping stage, not an output limiter.

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

## A node graph in code

A chain runs its stages in series. A **graph** runs chains side by side: `DspGraph` holds named nodes, each with its own
`DspChain`, and an edge carries one node's block into a later node. A node with several outgoing edges feeds every one,
which is the fan-out, and a node with several incoming edges sums them, which is the fan-in.

```csharp
DspGraph graph = new();
graph.TryAddNode(new DspChain(), out int input);
graph.TryAddNode(roomChain, out int room);
graph.TryAddNode(dryChain, out int dry);
graph.TryAddNode(new DspChain(), out int output);
graph.TryAddEdge(input, room);
graph.TryAddEdge(input, dry);
graph.TryAddEdge(room, output);
graph.TryAddEdge(dry, output);
graph.TryPrepare(AudioSettings.outputSampleRate, 1024, 2, input, output);
graph.Process(buffer, 0, buffer.Length, 2);
```

- A branch that reports less delay is waited for: the graph delays that edge by the difference its chains report, so a
  pitch shifter in one branch and a limiter in another arrive together and sum without a comb. Read the graph's own
  delay with `LatencySamples`; it is the longest path.
- `TryPrepare` refuses a cycle, a node the input cannot reach or one that cannot reach the output, a layout it cannot
  run and a graph over its memory budget. It answers `false` and reserves nothing, so a bad graph never throws.
- A graph holds up to 16 nodes and 32 edges, and `PreparedBytes` reports what it reserved. `Process` allocates nothing
  once prepared: build the graph and prepare it before the first block.

## Limits

- A chain runs on the audio thread. Effects allocate when they are prepared, never while they run.
- A limiter delays the signal by its lookahead, and a pitch shifter by half its window. Read the delay with
  `AudioFilterHost.LatencySamples`; a chain does not compensate. Put the two in separate branches of a `DspGraph` and it
  aligns them for you.
- A graph is code-only: it has no asset and no inspector. Author the chains it holds as assets, then build the graph in
  code.
- A block whose shape no prepared chain matches passes through and counts in `AudioFilterHost.SkippedBlocks`, so a
  silent bypass is visible rather than guessed at.
