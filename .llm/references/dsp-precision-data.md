# DSP precision probe output

Summarized output from `dsp-precision-probe.cs.txt` on Linux ARM64 with .NET 10.0.15 and one pinned CPU.
The report tables use these rows. Repeated timing rows are in the separate logs under
`.artifacts/session-097/precision/`.

```text
host=b6a7f01e1aca os=Unix 6.18.58.1 arch=Arm64
dotnet=10.0.15 processors=1
seconds=0.01 coefficient=2.08116486E-003 float_final=0.999985695 double_final=1.000000000 max_error=1.431E-005 last_change_sample=5297 float_stalled=True
seconds=1.00 coefficient=2.08331167E-005 float_final=0.998569489 double_final=1.000000000 max_error=1.431E-003 last_change_sample=308820 float_stalled=True
seconds=60.00 coefficient=3.47222169E-007 float_final=0.914169312 double_final=0.999954600 max_error=8.579E-002 last_change_sample=6715854 float_stalled=True
seconds=600.00 coefficient=3.47222233E-008 float_final=0.500000000 double_final=0.632120577 max_error=1.321E-001 last_change_sample=16530831 float_stalled=True
seconds=3600.00 coefficient=5.78703707E-009 float_final=0.125000000 double_final=0.153518276 max_error=2.851E-002 last_change_sample=21919520 float_stalled=True
duration=1 fps=30 finished=True frames=30 float_elapsed=1.000000 exact_elapsed=1.000000 value=1.000000000 exact_value=1.000000000 error=1.110E-016
duration=1 fps=60 finished=True frames=61 float_elapsed=1.000000 exact_elapsed=1.016667 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=1 fps=144 finished=True frames=145 float_elapsed=1.000000 exact_elapsed=1.006944 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=1 fps=1000 finished=True frames=1001 float_elapsed=1.000000 exact_elapsed=1.001000 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=10 fps=30 finished=True frames=300 float_elapsed=10.000000 exact_elapsed=10.000000 value=1.000000000 exact_value=1.000000000 error=2.442E-015
duration=10 fps=60 finished=True frames=600 float_elapsed=10.000000 exact_elapsed=10.000000 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=10 fps=144 finished=True frames=1440 float_elapsed=10.000000 exact_elapsed=10.000000 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=10 fps=1000 finished=True frames=10000 float_elapsed=10.000000 exact_elapsed=10.000000 value=1.000000000 exact_value=1.000000000 error=1.033E-014
duration=60 fps=30 finished=True frames=1801 float_elapsed=60.000000 exact_elapsed=60.033333 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=60 fps=60 finished=True frames=3601 float_elapsed=60.000000 exact_elapsed=60.016667 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=60 fps=144 finished=True frames=8641 float_elapsed=60.000000 exact_elapsed=60.006944 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=60 fps=1000 finished=True frames=60022 float_elapsed=60.000000 exact_elapsed=60.022000 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=600 fps=30 finished=True frames=18002 float_elapsed=600.000000 exact_elapsed=600.066667 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=600 fps=60 finished=True frames=36007 float_elapsed=600.000000 exact_elapsed=600.116667 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=600 fps=144 finished=True frames=86312 float_elapsed=600.000000 exact_elapsed=599.388889 value=1.000000000 exact_value=0.998981481 error=1.019E-003
duration=600 fps=1000 finished=True frames=599472 float_elapsed=600.000000 exact_elapsed=599.472000 value=1.000000000 exact_value=0.999120000 error=8.800E-004
duration=3600 fps=30 finished=True frames=107854 float_elapsed=3600.000000 exact_elapsed=3595.133333 value=1.000000000 exact_value=0.998648148 error=1.352E-003
duration=3600 fps=60 finished=True frames=216169 float_elapsed=3600.000000 exact_elapsed=3602.816667 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=3600 fps=144 finished=True frames=521453 float_elapsed=3600.000000 exact_elapsed=3621.201389 value=1.000000000 exact_value=1.000000000 error=0.000E+000
duration=3600 fps=1000 finished=True frames=3671471 float_elapsed=3600.000000 exact_elapsed=3671.471000 value=1.000000000 exact_value=1.000000000 error=0.000E+000
rate=48000 hz=10.0 q=0.7071 stable=True peak=0.210015 peak_db=-13.55 max_error=9.206E-007 max_error_db=-120.72 rms_error=2.204E-007 error_below_peak_db=107.16
rate=48000 hz=10.0 q=20.0000 stable=True peak=0.418359 peak_db=-7.57 max_error=4.776E-005 max_error_db=-86.42 rms_error=8.041E-006 error_below_peak_db=78.85
rate=48000 hz=20.0 q=0.7071 stable=True peak=0.210015 peak_db=-13.56 max_error=6.407E-006 max_error_db=-103.87 rms_error=2.739E-006 error_below_peak_db=90.31
rate=48000 hz=20.0 q=20.0000 stable=True peak=0.418358 peak_db=-7.57 max_error=8.798E-005 max_error_db=-81.11 rms_error=1.031E-005 error_below_peak_db=73.54
rate=48000 hz=40.0 q=0.7071 stable=True peak=0.210013 peak_db=-13.56 max_error=6.027E-007 max_error_db=-124.40 rms_error=2.066E-007 error_below_peak_db=110.84
rate=48000 hz=40.0 q=20.0000 stable=True peak=0.418354 peak_db=-7.57 max_error=7.070E-005 max_error_db=-83.01 rms_error=6.014E-006 error_below_peak_db=75.44
rate=96000 hz=10.0 q=0.7071 stable=True peak=0.210015 peak_db=-13.55 max_error=1.778E-005 max_error_db=-95.00 rms_error=6.043E-006 error_below_peak_db=81.45
rate=96000 hz=10.0 q=20.0000 stable=True peak=0.418360 peak_db=-7.57 max_error=1.128E-004 max_error_db=-78.95 rms_error=2.652E-005 error_below_peak_db=71.38
rate=96000 hz=20.0 q=0.7071 stable=True peak=0.210015 peak_db=-13.55 max_error=9.206E-007 max_error_db=-120.72 rms_error=2.204E-007 error_below_peak_db=107.16
rate=96000 hz=20.0 q=20.0000 stable=True peak=0.418359 peak_db=-7.57 max_error=4.776E-005 max_error_db=-86.42 rms_error=8.041E-006 error_below_peak_db=78.85
rate=96000 hz=40.0 q=0.7071 stable=True peak=0.210015 peak_db=-13.56 max_error=6.407E-006 max_error_db=-103.87 rms_error=2.739E-006 error_below_peak_db=90.31
rate=96000 hz=40.0 q=20.0000 stable=True peak=0.418358 peak_db=-7.57 max_error=8.798E-005 max_error_db=-81.11 rms_error=1.031E-005 error_below_peak_db=73.54
rate=192000 hz=10.0 q=0.7071 stable=True peak=0.210015 peak_db=-13.55 max_error=2.949E-005 max_error_db=-90.61 rms_error=1.262E-005 error_below_peak_db=77.05
rate=192000 hz=10.0 q=20.0000 stable=True peak=0.418360 peak_db=-7.57 max_error=4.550E-004 max_error_db=-66.84 rms_error=1.219E-004 error_below_peak_db=59.27
rate=192000 hz=20.0 q=0.7071 stable=True peak=0.210015 peak_db=-13.55 max_error=1.778E-005 max_error_db=-95.00 rms_error=6.043E-006 error_below_peak_db=81.45
rate=192000 hz=20.0 q=20.0000 stable=True peak=0.418360 peak_db=-7.57 max_error=1.128E-004 max_error_db=-78.95 rms_error=2.652E-005 error_below_peak_db=71.38
rate=192000 hz=40.0 q=0.7071 stable=True peak=0.210015 peak_db=-13.55 max_error=9.206E-007 max_error_db=-120.72 rms_error=2.204E-007 error_below_peak_db=107.16
rate=192000 hz=40.0 q=20.0000 stable=True peak=0.418359 peak_db=-7.57 max_error=4.776E-005 max_error_db=-86.42 rms_error=8.041E-006 error_below_peak_db=78.85
rate=192000 hz=10.0 q=20.0000 samples=10000000 stable=True first_1m_max_error=4.550E-004 last_1m_max_error=8.747E-006 drift_ratio=0.02
compressor_release=600s coefficient=3.47222233E-008 float_gain_db=-12.000000 exact_gain_db=-10.858049 error_db=1.142E+000
compressor_actual_after_60s=-13.141494 double_state_prediction=-10.858049
limiter_release=600s float_gain=0.335830688 exact_gain=0.321371940 error=1.446E-002
release=0.01 coefficient=2.08116486E-003 error_after_1s_db=2.010E-045 error_after_60s_db=3.363E-043
release=0.10 coefficient=2.08311627E-004 error_after_1s_db=3.384E-010 error_after_60s_db=3.363E-042
release=1.00 coefficient=2.08331167E-005 error_after_1s_db=1.329E-005 error_after_60s_db=1.795E-029
release=10.00 coefficient=2.08333108E-006 error_after_1s_db=1.484E-004 error_after_60s_db=4.490E-005
release=60.00 coefficient=3.47222169E-007 error_after_1s_db=1.524E-002 error_after_60s_db=5.111E-002
release=600.00 coefficient=3.47222233E-008 error_after_1s_db=1.998E-002 error_after_60s_db=1.142E+000
hours=1 interval=0.010 accepted=334216 ideal=360000 early=0 late=51569 min_gap_steps=2 max_gap_steps=3 min_gap_ms=10.0 max_gap_ms=15.0
hours=1 interval=0.050 accepted=68544 ideal=72000 early=0 late=34561 min_gap_steps=10 max_gap_steps=11 min_gap_ms=50.0 max_gap_ms=55.0
hours=1 interval=0.100 accepted=34696 ideal=36000 early=0 late=26099 min_gap_steps=20 max_gap_steps=21 min_gap_ms=100.0 max_gap_ms=105.0
hours=1 interval=0.500 accepted=7200 ideal=7200 early=0 late=0 min_gap_steps=100 max_gap_steps=100 min_gap_ms=500.0 max_gap_ms=500.0
hours=8 interval=0.010 accepted=2127240 ideal=2880000 early=0 late=1505520 min_gap_steps=2 max_gap_steps=3 min_gap_ms=10.0 max_gap_ms=15.0
hours=8 interval=0.050 accepted=540574 ideal=576000 early=0 late=354262 min_gap_steps=10 max_gap_steps=11 min_gap_ms=50.0 max_gap_ms=55.0
hours=8 interval=0.100 accepted=276616 ideal=288000 early=0 late=227697 min_gap_steps=20 max_gap_steps=21 min_gap_ms=100.0 max_gap_ms=105.0
hours=8 interval=0.500 accepted=57600 ideal=57600 early=0 late=0 min_gap_steps=100 max_gap_steps=100 min_gap_ms=500.0 max_gap_ms=500.0
hours=24 interval=0.010 accepted=6353799 ideal=8640000 early=0 late=4405489 min_gap_steps=2 max_gap_steps=4 min_gap_ms=10.0 max_gap_ms=20.0
hours=24 interval=0.050 accepted=1607826 ideal=1728000 early=0 late=1201743 min_gap_steps=10 max_gap_steps=11 min_gap_ms=50.0 max_gap_ms=55.0
hours=24 interval=0.100 accepted=840537 ideal=864000 early=0 late=469275 min_gap_steps=20 max_gap_steps=21 min_gap_ms=100.0 max_gap_ms=105.0
hours=24 interval=0.500 accepted=172800 ideal=172800 early=0 late=0 min_gap_steps=100 max_gap_steps=100 min_gap_ms=500.0 max_gap_ms=500.0
hours=72 interval=0.010 accepted=17413000 ideal=25920000 early=0 late=15464689 min_gap_steps=2 max_gap_steps=4 min_gap_ms=10.0 max_gap_ms=20.0
hours=72 interval=0.050 accepted=4474734 ideal=5184000 early=0 late=4017598 min_gap_steps=10 max_gap_steps=13 min_gap_ms=50.0 max_gap_ms=65.0
hours=72 interval=0.100 accepted=2451840 ideal=2592000 early=0 late=1778183 min_gap_steps=20 max_gap_steps=22 min_gap_ms=100.0 max_gap_ms=110.0
hours=72 interval=0.500 accepted=518401 ideal=518400 early=1 late=0 min_gap_steps=99 max_gap_steps=100 min_gap_ms=495.0 max_gap_ms=500.0
voices=1 float_p50_us=17.50 float_p95_us=17.70 double_p50_us=18.10 double_p95_us=21.30
voices=8 float_p50_us=132.00 float_p95_us=168.10 double_p50_us=141.20 double_p95_us=195.70
alloc_after_warm_1000_blocks_float8=0
```
