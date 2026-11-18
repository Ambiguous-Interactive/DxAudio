namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Holds a signal under a ceiling by lowering its gain before the loudest sample arrives.</summary>
    /// <remarks>
    /// A limiter that reacts on the sample it hears is too late: the peak has already left for the output. So the
    /// signal is delayed by the lookahead, the gain is lowered on the way in, and the loud sample reaches the
    /// output already turned down. The delay is reported as <see cref="LatencySamples"/>.
    ///
    /// Attack is instant because the lookahead did the anticipation. Only the release is smoothed: a gain that
    /// jumped back up between two peaks would pump the level audibly.
    /// </remarks>
    public sealed class DspLimiter : DspEffect
    {
        /// <summary>A ceiling that leaves room for inter-sample peaks, in dBFS.</summary>
        public const float DefaultCeilingDecibels = -1f;

        private const float QuietestCeiling = 0.0001f;
        private const float MaximumLookaheadSeconds = 1f;

        private float _ceiling;
        private float _lookaheadSeconds;
        private float _releaseSeconds;
        private float[] _delay;
        private int _delayFrames;
        private int _delayIndex;
        private int _channels;
        private int _sampleRate;
        private float _releaseCoefficient;
        private float _gain;
        private int _holdFrames;

        public DspLimiter()
        {
            _ceiling = AudioMath.DecibelsToLinear(DefaultCeilingDecibels);
            _lookaheadSeconds = 0.005f;
            _releaseSeconds = 0.05f;
            _delay = Array.Empty<float>();
            _gain = 1f;
        }

        public override string DisplayName => "Limiter";

        /// <summary>The loudest sample that leaves the limiter, as a linear gain.</summary>
        /// <remarks>Clamped between a hair above silence and full scale; a limiter never lifts a signal.</remarks>
        public float Ceiling
        {
            get => _ceiling;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _ceiling = AudioMath.Clamp(value, QuietestCeiling, 1f);
                }
            }
        }

        /// <summary>How far ahead the limiter lowers its gain, in seconds.</summary>
        /// <remarks>
        /// It sizes the delay line, so a new value applies at the next <see cref="Prepare"/>. It is clamped to one
        /// second: a longer delay is a wrong setting, not a limiter.
        /// </remarks>
        public float LookaheadSeconds
        {
            get => _lookaheadSeconds;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _lookaheadSeconds = AudioMath.Clamp(value, 0f, MaximumLookaheadSeconds);
                }
            }
        }

        /// <summary>How quickly the gain returns to unity after a peak, in seconds.</summary>
        public float ReleaseSeconds
        {
            get => _releaseSeconds;
            set
            {
                if (!AudioMath.IsFinite(value))
                {
                    return;
                }

                _releaseSeconds = Math.Max(0f, value);
                _releaseCoefficient = AudioMath.OnePoleCoefficient(_releaseSeconds, _sampleRate);
            }
        }

        public override int LatencySamples => _delayFrames;

        /// <summary>How far the gain is currently held down, in decibels; zero is not limiting.</summary>
        public float GainReductionDecibels => AudioMath.LinearToDecibels(_gain);

        public override void Prepare(int sampleRate, int maxBlockSize, int channels)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (channels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            _sampleRate = sampleRate;
            _channels = channels;
            _delayFrames = (int)Math.Round(_lookaheadSeconds * sampleRate);
            _delay = 0 < _delayFrames ? new float[_delayFrames * channels] : Array.Empty<float>();
            _releaseCoefficient = AudioMath.OnePoleCoefficient(_releaseSeconds, sampleRate);
            Reset();
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            int stride = channels;

            int frames = count / stride;
            for (int frame = 0; frame < frames; frame++)
            {
                int frameStart = offset + (frame * stride);
                int slot = _delayIndex * stride;
                float incomingPeak = 0f;
                float outgoingPeak = 0f;
                for (int channel = 0; channel < stride; channel++)
                {
                    float incoming = Math.Abs(buffer[frameStart + channel]);
                    if (incomingPeak < incoming)
                    {
                        incomingPeak = incoming;
                    }

                    // The frame that leaves now was measured by an earlier block, so a ceiling lowered since
                    // then has to be enforced against it as well.
                    if (0 < _delayFrames)
                    {
                        float outgoing = Math.Abs(_delay[slot + channel]);
                        if (outgoingPeak < outgoing)
                        {
                            outgoingPeak = outgoing;
                        }
                    }
                }

                // The hold keeps the gain steady while a peak travels the lookahead, so only an arriving peak
                // renews it. The countdown still runs whenever nothing renews it, even while a peak in flight holds
                // the gain down; otherwise a sustained passage would extend the hold by its own length.
                if (_ceiling < incomingPeak)
                {
                    _holdFrames = _delayFrames;
                }
                else if (0 < _holdFrames)
                {
                    _holdFrames--;
                }

                float peak = outgoingPeak < incomingPeak ? incomingPeak : outgoingPeak;
                if (_ceiling < peak)
                {
                    // The frame that leaves is measured against the ceiling of its own block too, so a ceiling
                    // lowered since it was stored still holds.
                    float neededGain = _ceiling / peak;
                    if (neededGain < _gain)
                    {
                        _gain = neededGain;
                    }
                }
                else if (0 == _holdFrames)
                {
                    _gain += (1f - _gain) * _releaseCoefficient;
                }

                if (0 < _delayFrames)
                {
                    for (int channel = 0; channel < stride; channel++)
                    {
                        int index = frameStart + channel;
                        float delayed = _delay[slot + channel];
                        _delay[slot + channel] = buffer[index];
                        buffer[index] = delayed * _gain;
                    }

                    _delayIndex++;
                    if (_delayIndex == _delayFrames)
                    {
                        _delayIndex = 0;
                    }
                }
                else
                {
                    for (int channel = 0; channel < stride; channel++)
                    {
                        int index = frameStart + channel;
                        buffer[index] *= _gain;
                    }
                }
            }
        }

        public override void Reset()
        {
            _gain = 1f;
            _holdFrames = 0;
            _delayIndex = 0;
            if (_delay != null)
            {
                Array.Clear(_delay, 0, _delay.Length);
            }
        }
    }
}
