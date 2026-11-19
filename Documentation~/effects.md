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

The filter shapes one band around a corner. Every mode reads the same two-integrator core, so a mode change swaps which
mix of the low, band and high outputs leaves the stage. The output itself steps, so change the mode at a quiet moment.

| Mode      | What leaves the stage                                      | A game use                         |
| --------- | ---------------------------------------------------------- | ---------------------------------- |
| Low Pass  | Below the corner, rolling off above it                     | Muffled, underwater, behind a wall |
| High Pass | Above the corner, rolling off below it                     | Thin, distant, through a wall      |
| Band Pass | A window around the corner                                 | Radio, telephone, a small speaker  |
| Notch     | Everything except a window around the corner               | Remove a hum or a whistle          |
| Peak      | The whole sound, with the band lifted by **Peak Decibels** | Add presence or air                |
| Bell      | The whole sound, with the band moved by **Peak Decibels**  | An equalizer bell for tone shaping |

- **Frequency** is the corner, from 10 Hz to 20 kHz. The prepared rate caps it at 45% of the sample rate, so a corner
  above Nyquist is a corner just under it rather than an unstable coefficient.
- **Quality** is how narrow the band is. 0.71 is flat at the corner; a higher value narrows the band and lifts the
  corner of a low pass or a high pass. A band pass stays at unity in its centre, and a notch stays a null, whatever the
  quality; those two only narrow.
- **Peak Decibels** moves only the Peak and Bell bands, from -24 to 24 dB. A gain already at zero, as after
  preparation or a reset, passes the block through sample for sample; a move from a nonzero gain ramps to zero. Peak's
  poles do not move with the gain, so a deep cut is much narrower than a large boost. Bell moves its damping with the
  gain, so a boost and a cut of the same size have the same width; at the same quality a boost is narrower than Peak's
  and a cut is wider. Moving a published Peak asset to Bell changes its sound, so keep Peak for existing content.
- A knob moved while the sound plays is smoothed with a ten-millisecond time constant, so a sweep does not click. Set the
  corner before playback when the exact first block matters.

Measured at 48 kHz with a 1 kHz corner: a low pass leaves a 100 Hz tone at 0.0 dB and an 8 kHz tone at -37.8 dB, a
high pass leaves 5 kHz at 0.0 dB and 125 Hz at -36.1 dB, a band pass with quality 2 leaves its centre at 0.0 dB and a
tone an octave away at -10 dB, and a notch at quality 2 leaves its centre below -120 dB. A bell at quality 2 reaches
6.0 dB for a +6 dB setting, 2.0 dB at 700 Hz and 0.6 dB at 2 kHz; at +24 dB its 700 Hz skirt is 9.1 dB and at -24 dB it
is -9.1 dB, the mirror the Peak mode does not give.

## Multiband equalizer

To fit a drawn response, select a **DSP Chain** asset and open **Drawn EQ** in the Inspector.

1. Select the output sample rate and a band limit. Drag across the plot in either direction or load one of the
   example targets.
2. Select **Fit target**. Compare the combined Bell response with the target on the 20 Hz to 20 kHz logarithmic
   axis. The Inspector reports RMS error and the largest sampled error in dB. Compare different band limits and
   use the one that meets your target.
3. Select **Save new EQ chain**. The new asset contains its Bell stages as sub-assets. The drawing is temporary.

The Inspector starts at Unity's reported output rate when it is available. The drawing always spans 20 Hz to 20 kHz.
At lower rates, the shaded part of the plot lies above the fit range. Both curves keep their frequency positions.
The fitter measures the covered range on 2,049 log-spaced points. It also checks the Bell centers for the maximum error.
For sharp targets, inspect the fitted curve between draw points. A large difference there can also raise the output peak.

You can also create one **Filter** asset per band and set its mode to **Bell**. Put the filters in a **Chain** so the signal passes
through all bands in order. Assign the chain to a cue or track for local shaping, or use it on an [Audio Mix Bus](#a-mix-bus)
for the listener mix. Inside a graph, put the whole chain in one node on the signal path.

These starting points use **Quality** 1 on every band. Set **Peak Decibels** to 0 for a flat start and adjust the bands
by ear. The same setup works with other band counts or center frequencies.

| Bands | Center frequencies (Hz)                                           |
| ----- | ----------------------------------------------------------------- |
| 7     | 63, 125, 250, 500, 1,000, 2,000, 4,000                            |
| 11    | 31, 63, 125, 250, 500, 1,000, 2,000, 4,000, 8,000, 12,000, 16,000 |

Nearby bands overlap. At any given frequency, their gains add in decibels, including the response away from each
band's center. Each Bell also passes the dry signal, so splitting the bands across parallel graph branches and summing
them adds copies of the dry signal.

Leave room for boosts. Lower the level before the EQ and check the output peak with your actual sounds. Use a limiter after
the EQ if the output needs a sample ceiling, and allow for its lookahead delay. A ceiling on a cue or track applies
to that voice. A ceiling on the listener mix applies to the summed output.

For example, set the gains below in center-frequency order. Unity measured the combined response by feeding one tone
at a time through each chain. The largest measured boost fell between two band centers in both examples.

| Bands | Gains in frequency order (dB)          | Measured response (dB) |
| ----- | -------------------------------------- | ---------------------- |
| 7     | +3, 0, -3, -2, 0, +4, +3               | -3.37 to +4.79         |
| 11    | 0, +3, 0, -3, -2, 0, +4, +3, -2, 0, +2 | -3.37 to +4.66         |

With two tones playing together, the dry peak was 0.693 on a scale where 1.0 is full scale. The seven-band chain
raised that peak to 1.278, and the eleven-band chain raised it to 1.245. Lowering this input by 6 dB brought the peaks
to 0.641 and 0.624. Check each sound's peak to choose its trim.

### Drawn EQ fit error

The table shows maximum sampled error / RMS error in dB in Unity 6000.6.7f1. The fit covers 20 Hz to 20 kHz,
except at 32 kHz, where it ends at 15.68 kHz.
The **Flat** target used zero stages with 0 dB error at every rate and band limit.

| Target             | Preview rate (kHz) | 1 band        | 3 bands       | 7 bands       | 11 bands      |
| ------------------ | ------------------ | ------------- | ------------- | ------------- | ------------- |
| Broad low shelf    | 32                 | 1.204 / 0.501 | 0.513 / 0.093 | 0.041 / 0.008 | 0.009 / 0.003 |
| Broad low shelf    | 44.1               | 1.197 / 0.492 | 0.534 / 0.095 | 0.042 / 0.008 | 0.023 / 0.003 |
| Narrow notch       | 32                 | 1.820 / 0.272 | 0.904 / 0.113 | 0.494 / 0.044 | 0.267 / 0.021 |
| Narrow notch       | 44.1               | 1.934 / 0.270 | 1.341 / 0.121 | 0.823 / 0.047 | 0.713 / 0.038 |
| Overlapping boosts | 32                 | 1.340 / 0.561 | 0.439 / 0.167 | 0.097 / 0.028 | 0.024 / 0.008 |
| Overlapping boosts | 44.1               | 1.342 / 0.553 | 0.464 / 0.167 | 0.091 / 0.025 | 0.020 / 0.007 |
| Broad low shelf    | 48                 | 1.197 / 0.492 | 0.534 / 0.095 | 0.042 / 0.008 | 0.020 / 0.003 |
| Narrow notch       | 48                 | 2.084 / 0.272 | 1.317 / 0.119 | 0.761 / 0.041 | 0.687 / 0.036 |
| Overlapping boosts | 48                 | 1.335 / 0.554 | 0.455 / 0.168 | 0.061 / 0.021 | 0.021 / 0.007 |
| Broad low shelf    | 96                 | 1.197 / 0.492 | 0.534 / 0.095 | 0.043 / 0.008 | 0.012 / 0.003 |
| Narrow notch       | 96                 | 2.059 / 0.272 | 1.347 / 0.121 | 0.848 / 0.047 | 0.709 / 0.038 |
| Overlapping boosts | 96                 | 1.335 / 0.555 | 0.462 / 0.171 | 0.069 / 0.023 | 0.020 / 0.007 |

The narrow notch retained up to 0.71 dB of peak error even at 11 bands. Its target changes sharply between points.

### Drawn EQ in a standalone player

A Unity 6000.6.7f1 release player used Windows x64 Mono on a Snapdragon X Elite X1E80100 with Windows 11.
The native audio callback ran at 48 kHz with stereo blocks of 1,024 frames and four buffers. It processed exported
**Overlapping boosts** chains and sine tones at 656.25 and 1,078.125 Hz. Each tone used 0.32 of full scale.
For each band limit, the probe timed 240 blocks across two passes in reverse order, after 32 warm blocks per pass.

| Bell stages | Median / p95 chain time (us) | Raw sample peak (dBFS) |
| ----------- | ---------------------------- | ---------------------- |
| 0           | 0.90 / 1.40                  | -3.895                 |
| 3           | 278.60 / 357.30              | +4.927                 |
| 7           | 740.95 / 834.60              | +5.108                 |
| 11          | 1,084.55 / 1,283.40          | +5.137                 |

The two tone gains matched the preview within 0.00001 dB for every chain. All fitted chains exceeded full scale
before the probe applied its listening trim. Choose the band limit for the required fit and the cost on the target
hardware. Check the peak with the actual audio before deciding how much input trim to apply.

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
