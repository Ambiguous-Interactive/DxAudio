# DSP filter

The filter is one topology-preserving state-variable core with six outputs: low pass, high pass, band pass, notch, peak
and bell. Two integrators per channel feed it, and its coefficients follow the corner each frame, so a moving corner
never swaps one set of poles for another; corner, quality and peak gain are smoothed over a ten-millisecond time
constant. A corner is capped at 45% of the sample rate, so a tangent never runs away. A non-finite input sample latches
both integrators until `Reset`, because the core feeds its own state back, and a per-frame guard would buy nothing.

The mode reads once per block and is a choice of output, not a coefficient, except that a peak and a bell mix the band
under the dry signal. With `v0` the input, `v1` the band output, `v2` the low output, `Q` the quality, `k = 1 / Q` and
`G` the linear gain from `PeakDecibels`:

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

Bell's 2 kHz skirt mirrors its 700 Hz skirt, and Peak's does not: at -24 dB its 700 Hz reading is 1.7 dB down where its
boost of the same size is 19.1 dB up.

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
