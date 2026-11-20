namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Moves a signal up or down in pitch by resampling a delay line, with no library and no FFT.</summary>
    /// <remarks>
    /// Two read heads travel through one delay line at the rate the pitch asks for. A head that moves faster than the
    /// write plays the recorded signal back faster, which raises its pitch; slower lowers it. Each head sweeps a window
    /// of <see cref="WindowSeconds"/> and then jumps back to its start, so the two heads are half a window apart and a
    /// triangular crossfade keeps the sum continuous: the head that jumps is always the one at zero gain.
    ///
    /// This is a granular shift, not a phase vocoder. It keeps transients and needs no transform, at the cost of a
    /// fixed delay of half a window and a little warble on a pure tone. A shorter window lowers the delay and adds
    /// warble; a longer one smooths the tone and delays it more.
    ///
    /// At unison the two heads would sit at different delays and comb the sound, so the sweep parks at the point where
    /// one head is silent and the other reads at exactly <see cref="LatencySamples"/>. A shift of zero therefore delays
    /// the signal and leaves it otherwise untouched. Returning to unison crossfades the mix into that parked read over
    /// ten milliseconds, so a jump to zero never splices the waveform.
    /// </remarks>
    public sealed class DspPitchShifter : DspEffect
    {
        /// <summary>The lowest pitch this stage reaches, in semitones.</summary>
        public const float MinimumSemitones = -24f;

        /// <summary>The highest pitch this stage reaches, in semitones.</summary>
        public const float MaximumSemitones = 24f;

        /// <summary>A window that trades a little warble for a delay of about twenty-five milliseconds.</summary>
        public const float DefaultWindowSeconds = 0.05f;

        public const float MinimumWindowSeconds = 0.005f;

        public const float MaximumWindowSeconds = 0.2f;

        /// <summary>How close to unison counts as unison, in semitones; one cent is the smallest step most people hear.</summary>
        private const float UnisonToleranceSemitones = 0.01f;

        /// <summary>The delay of the nearest readable frame, which keeps every tap behind its write.</summary>
        private const float MinimumDelayFrames = 1f;

        /// <summary>How long a jump to or from unison takes to crossfade, in seconds.</summary>
        /// <remarks>
        /// The two regimes read the line at different delays, so switching between them in one frame splices the wave.
        /// Ten milliseconds is short enough to feel immediate and long enough to hide the change.
        /// </remarks>
        private const float TransitionSeconds = 0.01f;

        public override string DisplayName => "Pitch Shifter";

        /// <summary>How far the stage moves the pitch, in semitones; zero delays the signal and changes nothing else.</summary>
        public float Semitones
        {
            get => _semitones;
            set
            {
                if (!AudioMath.IsFinite(value))
                {
                    return;
                }

                _semitones = AudioMath.Clamp(value, MinimumSemitones, MaximumSemitones);
                /*
                    One ratio is the whole setting: a block reads it once and gets both the mode and the speed from it,
                    so a knob move cannot leave the two disagreeing inside a block.
                */
                _ratio = UnisonToleranceSemitones < Math.Abs(_semitones) ? (float)Math.Pow(2.0, _semitones / 12.0) : 1f;
            }
        }

        /// <summary>How long each read head sweeps before it jumps back, in seconds.</summary>
        /// <remarks>
        /// The delay line is sized at <see cref="TryPrepare"/> and not rebuilt here, because a setter can run on any thread
        /// while the audio thread is inside <see cref="Process"/>. The delay is half this window, which is why a short
        /// window is the only way to lower the latency.
        /// </remarks>
        public float WindowSeconds
        {
            get => _windowSeconds;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _windowSeconds = AudioMath.Clamp(value, MinimumWindowSeconds, MaximumWindowSeconds);
                }
            }
        }

        /// <summary>
        /// Half a window plus the nearest readable frame, in samples; zero before <see cref="TryPrepare"/>.
        /// </summary>
        /// <remarks>
        /// One figure covers both settings. At unison the signal is delayed by exactly this much. While shifting, the
        /// heads sweep from the nearest frame to a whole window, so the mean delay is half a window.
        /// </remarks>
        public override int LatencySamples => _latencyFrames;

        private float _semitones;
        private float _windowSeconds;
        private float _ratio;
        private int _channels;
        private int _windowFrames;
        private int _halfWindowFrames;
        private int _capacityFrames;
        private int _latencyFrames;
        private int _writeFrame;
        private double _phase;
        private double _parkBlend;
        private double _parkStep;
        private float[] _line;

        public DspPitchShifter()
        {
            _semitones = 0f;
            _windowSeconds = DefaultWindowSeconds;
            _ratio = 1f;
            _line = Array.Empty<float>();
        }

        /// <summary>A triangular window over one sweep, so a head is silent at the start and the end of its pass.</summary>
        private static float TriangleGain(double phase, int windowFrames)
        {
            double doubled = 2.0 * (phase / windowFrames);
            return doubled <= 1.0 ? (float)doubled : (float)(2.0 - doubled);
        }

        /// <summary>Keeps a sweep inside one window, so a head never reads a frame that is more than a window old.</summary>
        /// <remarks>
        /// A tap at delay zero is at zero gain, so the jump back to the start of the sweep is inaudible. The loop is
        /// bounded by the step, which stays under one window because the ratio is clamped to a quarter and four.
        /// </remarks>
        private static double WrapPhase(double phase, int windowFrames)
        {
            while (phase < 0.0)
            {
                phase += windowFrames;
            }

            while (windowFrames <= phase)
            {
                phase -= windowFrames;
            }

            return phase;
        }

        /// <summary>Moves a resting sweep to the nearest point where one head is silent and the other reads at the delay.</summary>
        private static double NearestParkPhase(double phase, int windowFrames, int halfWindowFrames)
        {
            double toStart = Math.Min(phase, windowFrames - phase);
            return Math.Abs(phase - halfWindowFrames) <= toStart ? halfWindowFrames : 0.0;
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            /*
                One setting for the whole block, so a knob set on the main thread cannot split a block between two
                speeds. Every tap position advances per frame either way, so block size never changes the sound.
            */
            float ratio = _ratio;
            bool shifting = 1f != ratio;
            double step = shifting ? 1.0 - ratio : 0.0;
            int frames = count / channels;
            int stride = channels;
            for (int frame = 0; frame < frames; frame++)
            {
                int frameStart = offset + (frame * stride);
                int writeStart = _writeFrame * stride;
                for (int channel = 0; channel < stride; channel++)
                {
                    _line[writeStart + channel] = buffer[frameStart + channel];
                }

                if (_parkBlend <= 0.0)
                {
                    for (int channel = 0; channel < stride; channel++)
                    {
                        buffer[frameStart + channel] = Read(_latencyFrames, channel, stride);
                    }
                }
                else
                {
                    float firstDelay = (float)_phase + MinimumDelayFrames;
                    float secondDelay = (float)WrapPhase(_phase + _halfWindowFrames, _windowFrames) + MinimumDelayFrames;
                    float firstGain = TriangleGain(_phase, _windowFrames);
                    float secondGain = 1f - firstGain;
                    // A jump to or from unison would splice the wave, so the settled read takes over over a short fade.
                    float mix = (float)_parkBlend;
                    float parkedGain = 1f - mix;
                    for (int channel = 0; channel < stride; channel++)
                    {
                        float shifted = (firstGain * Read(firstDelay, channel, stride)) + (secondGain * Read(secondDelay, channel, stride));
                        float parked = Read(_latencyFrames, channel, stride);
                        buffer[frameStart + channel] = (mix * shifted) + (parkedGain * parked);
                    }
                }

                if (shifting)
                {
                    _phase = WrapPhase(_phase + step, _windowFrames);
                    if (_parkBlend < 1.0)
                    {
                        _parkBlend = Math.Min(1.0, _parkBlend + _parkStep);
                    }
                }
                else if (0.0 < _parkBlend)
                {
                    _parkBlend = Math.Max(0.0, _parkBlend - _parkStep);
                    if (_parkBlend == 0.0)
                    {
                        // The output is already the settled read, so moving the sweep now changes nothing heard.
                        _phase = NearestParkPhase(_phase, _windowFrames, _halfWindowFrames);
                    }
                }

                _writeFrame++;
                if (_writeFrame == _capacityFrames)
                {
                    _writeFrame = 0;
                }
            }
        }

        public override void Reset()
        {
            _writeFrame = 0;
            /*
                A fresh voice starts parked: one head is silent and the other reads the signal back at exactly the
                reported delay, so a pooled voice does not fade in from a stale sweep.
            */
            _phase = 0.0;
            _parkBlend = 0.0;
            if (_line != null)
            {
                Array.Clear(_line, 0, _line.Length);
            }
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            _channels = channels;
            /*
                Two heads sit half a window apart, so an even window makes that gap a whole number of frames. The two
                spare frames hold the write and the interpolated neighbour apart at the longest delay.
            */
            int window = (int)Math.Round(_windowSeconds * sampleRate);
            if (window < 2)
            {
                window = 2;
            }

            if (0 < (window & 1))
            {
                window++;
            }

            _windowFrames = window;
            _halfWindowFrames = window / 2;
            _capacityFrames = window + 2;
            _latencyFrames = _halfWindowFrames + (int)MinimumDelayFrames;
            _parkStep = 1.0 / (TransitionSeconds * sampleRate);
            _line = new float[_capacityFrames * channels];
            Reset();
        }

        /// <summary>Reads one channel at a fractional delay behind the write, interpolating between the two frames.</summary>
        private float Read(float delayFrames, int channel, int stride)
        {
            float position = _writeFrame - delayFrames;
            while (position < 0f)
            {
                position += _capacityFrames;
            }

            int lower = (int)position;
            float fraction = position - lower;
            int upper = lower + 1;
            if (_capacityFrames == upper)
            {
                upper = 0;
            }

            float first = _line[(lower * stride) + channel];
            float second = _line[(upper * stride) + channel];
            return first + ((second - first) * fraction);
        }
    }
}
