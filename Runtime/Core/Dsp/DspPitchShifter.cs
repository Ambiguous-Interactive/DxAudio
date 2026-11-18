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
    /// At unison the two heads would sit at different delays and comb the sound, so the effect collapses them onto one
    /// integer delay of exactly <see cref="LatencySamples"/>. A shift of zero therefore delays the signal and leaves it
    /// otherwise untouched.
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

        private float _semitones;
        private float _windowSeconds;
        private float _ratio;
        private bool _shifting;
        private int _sampleRate;
        private int _maxBlockSize;
        private int _channels;
        private int _windowFrames;
        private int _halfWindowFrames;
        private int _capacityFrames;
        private int _latencyFrames;
        private int _writeFrame;
        private float _phase;
        private float[] _line;

        public DspPitchShifter()
        {
            _semitones = 0f;
            _windowSeconds = DefaultWindowSeconds;
            _ratio = 1f;
            _shifting = false;
            _line = Array.Empty<float>();
        }

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
                _ratio = (float)Math.Pow(2.0, _semitones / 12.0);
                _shifting = UnisonToleranceSemitones < Math.Abs(_semitones);
            }
        }

        /// <summary>How long each read head sweeps before it jumps back, in seconds.</summary>
        /// <remarks>
        /// A change reallocates the delay line, so set it before audio starts. The delay is half this window, which is
        /// why a short window is the only way to lower the latency.
        /// </remarks>
        public float WindowSeconds
        {
            get => _windowSeconds;
            set
            {
                if (!AudioMath.IsFinite(value))
                {
                    return;
                }

                float clamped = AudioMath.Clamp(value, MinimumWindowSeconds, MaximumWindowSeconds);
                if (clamped == _windowSeconds)
                {
                    return;
                }

                _windowSeconds = clamped;
                if (0 < _sampleRate)
                {
                    Prepare(_sampleRate, _maxBlockSize, _channels);
                }
            }
        }

        /// <summary>
        /// Half a window plus the nearest readable frame, in samples; zero before <see cref="Prepare"/>.
        /// </summary>
        /// <remarks>
        /// One figure covers both settings. At unison the signal is delayed by exactly this much. While shifting, the
        /// heads sweep from the nearest frame to a whole window, so the mean delay is half a window.
        /// </remarks>
        public override int LatencySamples => _latencyFrames;

        public override void Prepare(int sampleRate, int maxBlockSize, int channels)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (maxBlockSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBlockSize));
            }

            if (channels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            _sampleRate = sampleRate;
            _maxBlockSize = maxBlockSize;
            _channels = channels;
            // Two heads sit half a window apart, so an even window makes that gap a whole number of frames. The two
            // spare frames hold the write and the interpolated neighbour apart at the longest delay.
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
            _line = new float[_capacityFrames * channels];
            Reset();
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            // One setting for the whole block, so a knob set on the main thread cannot split a block between two
            // speeds. Every tap position advances per frame either way, so block size never changes the sound.
            float step = _shifting ? 1f - _ratio : 0f;
            bool unison = !_shifting;
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

                if (unison)
                {
                    for (int channel = 0; channel < stride; channel++)
                    {
                        buffer[frameStart + channel] = Read(_latencyFrames, channel, stride);
                    }
                }
                else
                {
                    float firstDelay = _phase + MinimumDelayFrames;
                    float secondDelay = WrapPhase(_phase + _halfWindowFrames, _windowFrames) + MinimumDelayFrames;
                    float firstGain = TriangleGain(_phase, _windowFrames);
                    float secondGain = 1f - firstGain;
                    for (int channel = 0; channel < stride; channel++)
                    {
                        float sample = (firstGain * Read(firstDelay, channel, stride)) + (secondGain * Read(secondDelay, channel, stride));
                        buffer[frameStart + channel] = sample;
                    }

                    _phase = WrapPhase(_phase + step, _windowFrames);
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
            _phase = 0f;
            if (_line != null)
            {
                Array.Clear(_line, 0, _line.Length);
            }
        }

        /// <summary>A triangular window over one sweep, so a head is silent at the start and the end of its pass.</summary>
        private static float TriangleGain(float phase, int windowFrames)
        {
            float fraction = phase / windowFrames;
            float doubled = 2f * fraction;
            return doubled <= 1f ? doubled : 2f - doubled;
        }

        /// <summary>Keeps a sweep inside one window, so a head never reads a frame that is more than a window old.</summary>
        /// <remarks>
        /// A tap at delay zero is at zero gain, so the jump back to the start of the sweep is inaudible. The loop is
        /// bounded by the step, which stays under one window because the ratio is clamped to a quarter and four.
        /// </remarks>
        private static float WrapPhase(float phase, int windowFrames)
        {
            while (phase < 0f)
            {
                phase += windowFrames;
            }

            while (windowFrames <= phase)
            {
                phase -= windowFrames;
            }

            return phase;
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
