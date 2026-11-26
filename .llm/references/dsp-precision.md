# DSP numeric precision

Issue #185 asks where wider internal state improves DxAudio's rendered signal enough to justify its cost. This page
records the comparison of the production float paths against double-state prototypes. It is a decision record: no
production path changed here. The migration, if any, is a separate change.

## Method

- The probe compiles the shipped `Runtime/Core` sources with `net10.0` and calls the production `AudioMath`,
  `MusicRamp`, `DspFilter`, `DspCompressor`, `DspLimiter` and `SfxThrottle<TKey>` types.
- The double prototypes use the same equations and coefficients with `double` state and cast the output back to
  `float`. The signal input and output fixtures are identical.
- Host: Linux ARM64, .NET 10.0.15, one pinned CPU. The same probe ran three times for the timing rows.
- Filter signal cases use a 0.5 amplitude tone at twice the corner and one million samples. The long-run case uses ten
  million samples at 192 kHz. Envelope cases use 48 kHz.
- Filter CPU uses stereo 1,024-frame blocks, 64 warm-up blocks, and 240 measured blocks. Eight voices run as eight
  independent filters in one loop. Allocation uses the current thread's allocated-byte counter after 256 warm-up
  blocks and 1,000 measured blocks.
- Unity timing is not measured in this workspace. The Unity host bridge and editor are unavailable, so the CPU rows
  are a .NET proxy on the same Core source. A Unity run must confirm them before a migration.

The probe program and raw logs are in `.artifacts/session-097/precision/` (gitignored).

## One-pole state

A one-pole smoother uses the production coefficient. It moves from 0 to 1 for 600 seconds of samples at 48 kHz.

| Time constant | Coefficient   | Float final | Double final | Maximum error | Float stalls      |
| ------------- | ------------- | ----------- | ------------ | ------------- | ----------------- |
| 0.01 s        | 2.08116486e-3 | 0.999985695 | 1.000000000  | 1.431e-5      | yes, after 0.11 s |
| 1 s           | 2.08331167e-5 | 0.998569489 | 1.000000000  | 1.431e-3      | yes, after 6.4 s  |
| 60 s          | 3.47222169e-7 | 0.914169312 | 0.999954600  | 8.579e-2      | yes, after 140 s  |
| 600 s         | 3.47222233e-8 | 0.500000000 | 0.632120577  | 1.321e-1      | yes, after 344 s  |
| 3600 s        | 5.78703707e-9 | 0.125000000 | 0.153518276  | 2.851e-2      | yes, after 457 s  |

A float one-pole state stops moving when its step is below one float ulp. The stopped value is stable, not noisy.
The double state keeps moving. This matters only when a control can use a time constant of many seconds; a 0.01 s
smoother stalls at 0.999986, which is below one sample of audible difference at any normal signal level.

## Music ramps

`MusicRamp` accumulates elapsed time in float and clamps at its duration. Its final value is exact at every tested
duration and frame rate. The only visible effect is that a very long ramp can finish early.

| Duration | Frame rate           | Float finish | Exact finish   | Value error     |
| -------- | -------------------- | ------------ | -------------- | --------------- |
| 60 s     | 30, 60, 144, 1000 Hz | 60.000 s     | up to 60.022 s | 0 except 144 Hz |
| 600 s    | 144 Hz               | 600.000 s    | 599.389 s      | 1.019e-3        |
| 600 s    | 1000 Hz              | 600.000 s    | 599.472 s      | 8.800e-4        |
| 3600 s   | 30 Hz                | 3600.000 s   | 3595.133 s     | 1.352e-3        |

A 60 s fade is exact within the frame step. A one-hour fade can finish 0.13% early at 30 Hz. This is below a
perceptual step for a fade and does not justify double state. Keep `MusicRamp.ElapsedSeconds` in float.

## Recursive filter state

The filter is a topology-preserving state-variable filter. The float output is compared with the same filter using
double integrators and coefficients.

| Rate    | Corner | Quality | Peak dBFS | Maximum error | Error below peak |
| ------- | ------ | ------- | --------- | ------------- | ---------------- |
| 48 kHz  | 40 Hz  | 0.7071  | -13.56    | 6.027e-7      | 110.84 dB        |
| 48 kHz  | 10 Hz  | 20      | -7.57     | 4.776e-5      | 78.85 dB         |
| 96 kHz  | 10 Hz  | 20      | -7.57     | 1.128e-4      | 71.38 dB         |
| 192 kHz | 10 Hz  | 20      | -7.57     | 4.550e-4      | 59.27 dB         |

Every case stayed finite and stable. A ten-million-sample run at 192 kHz, 10 Hz and Q 20 had maximum error
4.550e-4 in the first million samples and 8.747e-6 in the last million, so the error does not grow with time. The
worst error sits about 59 dB below the signal peak. That is close enough to the noise floor of a quiet passage to
matter only when an extreme low corner and high quality are both used at a high sample rate. The normal 40 Hz,
Q 0.707 case is 111 dB below its peak, so float is enough there.

Double state costs 128 extra bytes for eight stereo filters (128 B against 256 B of state), before any double
sample buffer. It does not need a double buffer because the process writes float in place.

## Compressor and limiter envelopes

The compressor keeps its gain in float decibels and the limiter keeps its gain as a float linear value. The release
coefficient comes from `AudioMath.OnePoleCoefficient`, which is already computed in double. The table shows the
float-state error against a double state with the same coefficient, starting at -12 dB.

| Release | Error after 1 s | Error after 60 s |
| ------- | --------------- | ---------------- |
| 0.01 s  | 2.010e-45 dB    | 3.363e-43 dB     |
| 0.1 s   | 3.384e-10 dB    | 3.363e-42 dB     |
| 1 s     | 1.329e-5 dB     | 1.795e-29 dB     |
| 10 s    | 1.484e-4 dB     | 4.490e-5 dB      |
| 60 s    | 1.524e-2 dB     | 5.111e-2 dB      |
| 600 s   | 1.998e-2 dB     | 1.142 dB         |

After a 600 s release and 60 s of silence, the production compressor reports -13.141494 dB against -10.858049 dB
for the double envelope. The limiter gain is 0.335830688 against 0.321371940, a difference of 0.0145. A normal
release of 0.1 s or less is exact for any practical purpose. Releases of tens of seconds, which the public setters
currently accept, can stall in float and hold more reduction than the knob says. If long releases are a supported
use, move only those gain fields to double. A clamp on the documented release range is a smaller alternative.

## Rate-limit time

`SfxPlayer` passes `Time.unscaledTime` as a float to `SfxThrottle`. The probe compares that float path with an
integer-step reference at 5 ms resolution. An ideal rate limit accepts once per interval; a late acceptance means
the cue was dropped although its window had opened.

| Session | Interval | Accepted  | Ideal     | Late windows | Longest gap |
| ------- | -------- | --------- | --------- | ------------ | ----------- |
| 1 h     | 0.01 s   | 334,216   | 360,000   | 51,569       | 15 ms       |
| 8 h     | 0.01 s   | 2,127,240 | 2,880,000 | 1,505,520    | 15 ms       |
| 24 h    | 0.01 s   | 6,353,799 | 8,640,000 | 4,405,489    | 20 ms       |
| 72 h    | 0.05 s   | 4,474,734 | 5,184,000 | 4,017,598    | 65 ms       |

The float clock cannot hold a 10 ms window after an hour: about 14% of windows are late at 1 h and 26% at 8 h.
A 0.5 s window is unaffected until 72 h, where one window opened 5 ms early. The rate limit is a minimum, so a late
window is safe but changes how often a cue repeats. For rapid footsteps, impacts, or UI clicks this is audible.
The fix is a double time source (`Time.unscaledTimeAsDouble` or `AudioSettings.dspTime`) and a double throttle map.
That is a public time-unit change and must be a separate migration.

## CPU and allocation

Warmed render time for eight independent stereo filters, 1,024 frames, 48 kHz. Values are the range of three
pinned runs.

| Voices | Float p50      | Float p95      | Double p50     | Double p95     |
| ------ | -------------- | -------------- | -------------- | -------------- |
| 1      | 14.9-17.5 us   | 17.5-17.8 us   | 18.1-21.3 us   | 18.2-27.9 us   |
| 8      | 135.5-136.5 us | 161.0-175.5 us | 141.0-155.9 us | 165.7-206.1 us |

Double state adds about 20% at one voice and 4% to 15% at eight voices on this host. The production float path
measured 0 B across 1,000 warmed eight-voice blocks, so the current render path remains allocation-free. A double
sample buffer would add 16 KiB per 1,024-frame stereo voice (8 KiB float against 16 KiB double); the tested
prototype does not use one.

A second proxy compiled the same subset with `mcs` and ran it on Debian Mono 6.8.0.105, ARM64, one pinned CPU. One
run measured float p50 2,080 us and p95 2,480 us at one voice, and 19,090 us and 26,490 us at eight voices. The
double state measured 2,190/2,570 us and 20,230/24,170 us. Allocation was again 0 B. Repeated runs were noisy: the
eight-voice float p50 ranged from 19,120 to 31,880 us. Mono 6.8 is much slower than the Unity editor runtime and is
not a Unity timing, so it is not used for the decision. The noisier runs do not support a CPU claim either way.

## Decision

- Keep the rendered sample path and filter integrators in float. The error is inaudible for normal corners and
  qualities, the filter is stable for ten million samples, and a migration costs CPU for no measurable benefit in
  the normal range.
- Keep `MusicRamp` and its elapsed time in float. Its endpoint is exact and a long fade finishes within 0.14% of its
  duration.
- When long compressor or limiter releases are supported, use double for the gain state and their public time
  inputs. At 0.1 s and below the current float state is exact.
- Change the rate-limit clock before a game runs for hours with short intervals. `SfxThrottle` needs a double time
  source and double stored times; this is the clearest audible precision loss in the package.
- Keep double already used for phase, delay-line gains, reverb gain computation, and the noise-gate envelope.
  Those paths are not sample-rate state and their extra cost is already paid.
