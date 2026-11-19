# DSP filter

The filter is one topology-preserving state-variable core with six outputs: low pass, high pass, band pass, notch, peak
and bell. Two integrators per channel feed it, and its coefficients follow the corner each frame, so a moving corner
never swaps one set of poles for another; corner, quality and peak gain are smoothed over a ten-millisecond time
constant. A corner is capped at 45% of the sample rate, so a tangent never runs away. A non-finite input sample latches
both integrators until `Reset`, because the core feeds its own state back, and a per-frame guard would buy nothing.

The mode reads once per block and is a choice of output, not a coefficient, except that a peak and a bell mix the band
under the dry signal, and a bell also scales its damping by the gain. With `v0` the input, `v1` the band output, `v2`
the low output, `Q` the quality, `k = 1 / Q` and `G` the linear gain from `PeakDecibels`:

| Mode      | Output                    | Damping             |
| --------- | ------------------------- | ------------------- |
| Low pass  | `v2`                      | `k`                 |
| High pass | `v0 - k * v1 - v2`        | `k`                 |
| Band pass | `k * v1`                  | `k`                 |
| Notch     | `v0 - k * v1`             | `k`                 |
| Peak      | `v0 + (G - 1) * k * v1`   | `k`                 |
| Bell      | `v0 + (G - 1) * k_b * v1` | `k_b = k / sqrt(G)` |

Both gain modes reach exactly `G` at the corner, because the band output there reads `1 / damping`. Peak keeps its
damping, so only its zero moves with the gain and a deep cut is much narrower than a large boost. Bell is the textbook
equalizer bell: its poles move with the gain, so an equally sized boost and cut mirror in decibels. At the same quality
a bell boost is narrower than Peak's and a bell cut is wider, so switching a published Peak asset to Bell changes its
sound. Peak stays as it was and Bell is the new serialized value `6`, so the choice is additive and `0.3.x` assets keep
loading and sounding as they did. The manual's mode table is the author-facing copy of this decision.

## Measured response

48 kHz, 1 kHz corner, RMS over the last 100 ms of a 400 ms tone. The corner column is the tone at the corner, and the
octave columns are 500 Hz and 2 kHz; the far column is 8 kHz.

| Mode            | 500 Hz |  Corner |  2 kHz |  8 kHz |
| --------------- | -----: | ------: | -----: | -----: |
| Low pass, 0.71  |  -0.26 |   -3.01 | -12.37 | -37.80 |
| High pass, 0.71 | -12.32 |   -3.01 |  -0.26 |  -0.00 |
| Band pass, 2    | -10.01 |    0.00 | -10.06 | -24.82 |
| Notch, 2        |  -0.45 | -127.14 |  -0.44 |  -0.01 |
| Peak +6, 2      |  +1.13 |   +6.00 |  +1.12 |  +0.04 |
| Bell +6, 2      |  +0.63 |   +6.00 |  +0.63 |  +0.02 |

The skirt at quality 2, which is where the two gain modes differ:

| Setting | Peak at 700 Hz | Bell at 700 Hz | Bell at 2 kHz |
| ------- | -------------: | -------------: | ------------: |
| +24 dB  |          +19.1 |           +9.1 |          +4.3 |
| +12 dB  |           +7.6 |           +4.1 |          +1.5 |
| +6 dB   |           +2.9 |           +2.0 |          +0.6 |
| -6 dB   |           -1.2 |           -2.0 |          -0.6 |
| -12 dB  |           -1.5 |           -4.1 |          -1.5 |
| -24 dB  |           -1.7 |           -9.1 |          -4.3 |

Bell's cut mirrors its boost at every frequency, and Peak's does not: at -24 dB its 700 Hz reading is 1.7 dB down where
its boost of the same size is 19.1 dB up.

## Cost

At 48 kHz stereo with 256-frame blocks, after a 2,000-block warm-up, 20,000 timed blocks: Peak 2.05 us per block and
Bell 2.20 us, both 0 bytes allocated, so Bell costs about 7% more and still runs about 2,400x realtime. Only the bell
mode takes a square root, once per frame rather than once per sample. The 5.87 us reading that session 025 recorded is
not comparable: this container measures Peak at 2.05 us in the same probe today.

## Probe pitfalls

- A probe's last partial block must be a multiple of the block size, or the shape refusal leaves raw input in the tail it
  measures.
- Do not feed a filter its own output: a stable filter with a gain above one still grows in a feedback loop, so a sweep
  test uses a fresh block each pass.

## Multiband EQ

Use a serial Bell chain for both band counts in the [authoring guide](../../Documentation~/effects.md#multiband-equalizer).
A graph can use the same chain in one node. Each band has its own state, and the total response is the product of the
band magnitudes. In decibels, add the full response of each band at the requested frequency. A parallel sum would add
the dry signal from each band.

Unity 6000.6.7f1 EditMode on Windows 11, Snapdragon X Elite X1E80100. The probe built both paths from the filter and
graph assets at 48 kHz stereo with 256-frame blocks. It fed one second per integer tone at 0.125 amplitude and measured
RMS over the last half second. The 49 log-spaced tones from 25 Hz to 18 kHz plus the band centers gave 56 and 60 distinct
tones. Both paths produced identical samples. The maximum error against the ideal cascade rounded to 0.00002 dB.
The right channel stayed silent. A separate run with every gain at zero passed the input samples through unchanged.

For the reference response, let `r = tan(pi * f / fs) / tan(pi * fc / fs)`, where `f` is the tone frequency, `fc` is
the band center and `fs` is the sample rate. With linear gain `G`, use `x = 1 - r^2` and `y = r / (Q * sqrt(G))`.
The band response in decibels is `10 * log10((x^2 + G^2 * y^2) / (x^2 + y^2))`.

Both examples reached their largest sampled boost at 2,303 Hz. For the peak test, the probe summed 2 kHz and 4 kHz sines
at 0.4 amplitude each, with both starting at zero phase, and measured all samples from a reset. In a separate stress
test, setting every band to +6 dB at the same 1 kHz center gave +42 dB for seven bands and +66 dB for eleven bands on a
1 kHz tone. Those are measured tone gains. Mixed sources and transients need their own peak checks.

### Managed block cost

Median and p95 in microseconds per stereo block. After 400 warm-up blocks per path, the probe took 120 timing samples
per path, each averaging 16 blocks. It alternated path order and copied a fresh 1.5 kHz stereo tone block before each
call. The empty chain measures that copy and the call overhead.

| Signal path        | 256 frames, median / p95 (us) | 1,024 frames, median / p95 (us) |
| ------------------ | ----------------------------- | ------------------------------- |
| Empty chain        | 0.26 / 0.42                   | 0.49 / 0.96                     |
| 7 bands, chain     | 480.40 / 522.48               | 1,928.77 / 2,092.33             |
| 11 bands, chain    | 753.51 / 831.59               | 3,029.54 / 3,296.64             |
| 7 bands, one node  | 481.68 / 559.09               | 1,928.25 / 2,060.36             |
| 11 bands, one node | 760.40 / 837.88               | 3,025.22 / 3,236.66             |

Both paths report zero added latency. The graph reserves 2,048 bytes at 256 frames and 8,192 bytes at 1,024 frames for
its node buffer. These timings cover one managed processing path on the Editor thread. The eleven-band chain p95
of the averaged blocks was about 15% of a block period at both sizes. Budget per-voice use on the target device
with the expected number of active voices.
