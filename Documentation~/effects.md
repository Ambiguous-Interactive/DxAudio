---
doc_type: reference
---

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
| Delay         | Add an echo and decaying repeats after the dry sound            |
| Chorus        | Widen a sound with two copies that drift in pitch               |
| Tremolo       | Make the volume rise and fall in a steady rhythm                |
| Reverb        | Add a decaying room tail to a sound                             |
| Phaser        | Sweep two narrow dips through the sound                         |
| Noise Gate    | Fade out quiet tails while keeping stereo channels linked       |
| Stereo Width  | Narrow or widen the difference between stereo channels          |

## Author a chain

[Create and assign a chain](editor-tools.md#author-a-chain).

## A player-wide bus

Each player has one **Bus Chain**. It runs after every voice's own chain, so it is one place to shape the whole sound.

The player bus processes each voice before Unity mixes the output. Every bus effect, a limiter and a distortion included, acts on one voice at a time: two voices that each fit
under a ceiling can still sum above it. A bus is a shared shaping stage, not an output limiter. Use a mix bus for that.

## A mix bus

Unity sums every source before it calls an audio filter on the object that carries the `AudioListener`. An **Audio Mix
Bus** on that object therefore shapes the whole mix, including sources this package does not play.

See [Set up a DSP bus](editor-tools.md#set-up-a-dsp-bus).

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

## Delay

- **Delay Seconds** sets the spacing between repeats, from 0.25 to 500 ms. The effect rounds to the nearest frame.
  Prepare the chain again after changing the spacing in code.
- **Feedback** sends up to 90% of each repeat back into the delay. Zero gives one repeat.
- **Echo Level** sets the gain of the first repeat. The dry signal stays at full level and leaves immediately.
  Overlapping repeats can raise the peak above full scale. Leave headroom or add a limiter after the delay.

A voice must keep rendering for its repeats to play. For short clips, include silence at the end or put the delay
on the Audio Mix Bus. Reusing a voice clears its echo history. A 500 ms delay at 48 kHz uses 192,000 bytes for
stereo history. The voice also prepares any narrower channel layouts its host supports.

## Chorus

Chorus reads two copies from a short delay line. Their delay times move in opposite directions around 15 ms.
Left and right favor different copies, which widens a stereo signal without mixing sound between channels.
The dry signal leaves immediately when **Mix** includes it.

- **Rate Hertz** sets the modulation speed, from 0.05 to 5 cycles per second.
- **Depth Seconds** sets the maximum change from 15 ms, up to 8 ms. Zero gives a fixed delay.
- **Mix** sets how much of the result comes from the delayed copies. Zero leaves the dry signal unchanged.

These settings take effect when you prepare the chain. Reusing a voice restarts the modulation and discards its chorus history.

## Reverb

Reverb adds room reflections while the dry sound plays at full level. Use **Decay Seconds** to set the time for
the reflections to fall by 60 dB (0.15 to 3 seconds). Use **Wet Level** to set their level. Zero bypasses the effect.
The reflections soften high frequencies. The dry sound keeps its full bandwidth.

Each channel has its own tail. The voice must keep rendering for the tail to play. For short clips, include silence
at the end or put Reverb on the Audio Mix Bus. The dry sound and tail sum, so leave headroom for both.

In code, changes to either setting take effect when you next prepare the stage.

## Phaser

Phaser mixes the dry sound with a phase-shifted copy. The mix cancels two frequencies, and those dips sweep up and back.

- **Rate** sets the sweep speed from 0.05 to 5 cycles per second. The default is 0.5 Hz.
- **Depth** sets how deep the dips are. Zero passes the input through. One mixes equal parts dry and filtered audio. The default is 0.75.

Channels share the sweep and keep separate filter history. Phaser adds no latency.

## Tremolo

Tremolo lowers and restores the volume in a repeating triangle shape. It applies the same gain to every channel and adds no latency.

- **Rate** sets the number of cycles per second (0.05 to 20 Hz, default 4 Hz).
- **Depth** sets the largest drop from full gain. At 50%, the gain falls to half. At 100%, it falls to silence. At 0%, audio passes through unchanged.

Settings take effect when the chain is prepared. A reused voice starts the cycle at full gain.

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

See [Draw an EQ curve](editor-tools.md#draw-an-eq-curve) for the editor steps.

Applying a fit updates generated Bells in place, including Bells shared with other assets. A fit with fewer bands
removes surplus Bells from the signal path and keeps their sub-assets so existing references still work. Manually
added stages remain in the chain. The plot shows the generated EQ section of the chain.

The Inspector starts at Unity's reported output rate when it is available. The drawing always spans 20 Hz to 20 kHz.
At lower rates, the shaded part of the plot lies above the fit range. Both curves keep their frequency positions.
The fitter scores points between draw samples and uses more points near the top of the fit range.
It measures the result on 2,049 log-spaced points. The maximum error also includes Bell centers and visible draw points.

See [Build a Bell equalizer](editor-tools.md#build-a-bell-equalizer) for serial chain settings.

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

| Target             | Preview rate (kHz) | 1 band         | 3 bands        | 7 bands       | 11 bands      |
| ------------------ | ------------------ | -------------- | -------------- | ------------- | ------------- |
| 20 kHz spike       | 32                 | 0.000 / 0.000  | 0.000 / 0.000  | 0.000 / 0.000 | 0.000 / 0.000 |
| Broad low shelf    | 32                 | 1.200 / 0.501  | 0.569 / 0.102  | 0.061 / 0.010 | 0.013 / 0.004 |
| Narrow notch       | 32                 | 1.837 / 0.272  | 1.071 / 0.128  | 0.470 / 0.027 | 0.420 / 0.021 |
| Overlapping boosts | 32                 | 1.336 / 0.562  | 0.487 / 0.179  | 0.064 / 0.024 | 0.033 / 0.011 |
| 20 kHz spike       | 44.1               | 15.096 / 0.419 | 11.169 / 0.282 | 5.394 / 0.130 | 3.475 / 0.082 |
| Broad low shelf    | 44.1               | 1.210 / 0.492  | 0.582 / 0.107  | 0.058 / 0.009 | 0.012 / 0.003 |
| Narrow notch       | 44.1               | 1.901 / 0.268  | 1.024 / 0.118  | 0.474 / 0.027 | 0.405 / 0.022 |
| Overlapping boosts | 44.1               | 1.335 / 0.553  | 0.522 / 0.182  | 0.072 / 0.023 | 0.031 / 0.009 |
| 20 kHz spike       | 48                 | 7.480 / 0.268  | 2.826 / 0.145  | 0.598 / 0.026 | 0.356 / 0.013 |
| Broad low shelf    | 48                 | 1.210 / 0.492  | 0.582 / 0.107  | 0.058 / 0.009 | 0.012 / 0.003 |
| Narrow notch       | 48                 | 1.897 / 0.268  | 1.021 / 0.117  | 0.472 / 0.027 | 0.409 / 0.022 |
| Overlapping boosts | 48                 | 1.335 / 0.554  | 0.522 / 0.182  | 0.072 / 0.023 | 0.033 / 0.009 |
| 20 kHz spike       | 96                 | 8.131 / 0.397  | 2.300 / 0.114  | 1.525 / 0.050 | 1.495 / 0.049 |
| Broad low shelf    | 96                 | 1.211 / 0.492  | 0.582 / 0.107  | 0.058 / 0.009 | 0.012 / 0.003 |
| Narrow notch       | 96                 | 1.881 / 0.268  | 1.029 / 0.118  | 0.377 / 0.025 | 0.387 / 0.022 |
| Overlapping boosts | 96                 | 1.334 / 0.555  | 0.523 / 0.181  | 0.085 / 0.026 | 0.036 / 0.010 |

The last draw point is +24 dB in the **20 kHz spike** target. At 32 kHz it falls outside the fit range.
At 44.1 kHz with 11 bands, its sampled peak response fell from +44.89 dB to +21.94 dB.
With one band at that rate, RMS error decreased and maximum error increased. Compare both values when choosing the limit.

### Drawn EQ in a standalone player

A Unity 6000.6.7f1 release player used Windows x64 Mono on a Snapdragon X Elite X1E80100 with Windows 11.
This measurement used chains from the first version of the drawn EQ fitter.
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

See [Create a node graph](editor-tools.md#create-a-node-graph) for the editor and C# steps.

The graph runs after the cue's or track's chain and before the player's bus. A node's name is its identity because an
edge names it, so two nodes may not share one, and an edge to a name no node holds is refused rather than guessed at.
The cue inspector and the library check report a graph the runtime cannot build, which would otherwise pass the sound
through unshaped.

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

## Noise Gate

- **Threshold Decibels** sets the peak level that opens the gate, from -72 to 0 dBFS. The gate closes 6 dB below that level to avoid rapid switching near the threshold.
- **Attack Seconds** and **Release Seconds** set the opening and closing times from 0 to 2 seconds. Each time moves the gain about 63% toward its target. Zero changes the gain at once.
- The strongest channel in each frame controls one gain for all channels. The gate starts closed and adds no latency.
- Place it before a compressor to reduce quiet tails before adding makeup gain. Use a short attack to keep sharp transients.

## Stereo Width

Set **Width** to 0 to send the stereo average to both channels. Use 1 to keep the original sound.
Values between 0 and 1 narrow the sound. Values above 1 widen it, up to twice the original channel difference.
Leave headroom when widening because individual channels can peak higher. The stereo average stays the same.
The effect adds no latency. Mono and surround blocks pass through unchanged.
