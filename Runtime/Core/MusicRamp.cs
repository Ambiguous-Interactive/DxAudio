namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>A value moving from one level to another over time: a fade, or a duck being applied.</summary>
    /// <remarks>
    /// Time is passed in rather than read, so a ramp is a pure function of how long it has run and can be
    /// tested without a frame loop. A ramp of zero seconds is already finished: it lands on its target the
    /// first time it is read, rather than dividing by zero.
    /// </remarks>
    public struct MusicRamp
    {
        public float ElapsedSeconds => _elapsedSeconds;

        public bool IsFinished => seconds <= _elapsedSeconds;

        /// <summary>How far through the ramp it is, in [0, 1], before easing.</summary>
        public float Progress => seconds <= 0f ? 1f : AudioMath.Clamp01(_elapsedSeconds / seconds);

        public float Value => AudioMath.Lerp(from, to, MusicEasing.Evaluate(ease, Progress));
        public readonly float from;
        public readonly float to;
        public readonly float seconds;
        public readonly MusicEase ease;

        private float _elapsedSeconds;

        public MusicRamp(float from, float to, float seconds, MusicEase ease)
        {
            this.from = from;
            this.to = to;
            this.seconds = Math.Max(0f, seconds);
            this.ease = ease;
            _elapsedSeconds = 0f;
        }

        /// <summary>A ramp that already sits at <paramref name="value"/>.</summary>
        public static MusicRamp At(float value)
        {
            return new MusicRamp(value, value, 0f, MusicEase.Linear);
        }

        /// <summary>Moves the ramp on by <paramref name="deltaSeconds"/> and returns its value.</summary>
        public float Advance(float deltaSeconds)
        {
            if (0f < deltaSeconds)
            {
                _elapsedSeconds = Math.Min(seconds, _elapsedSeconds + deltaSeconds);
            }

            return Value;
        }

        /// <summary>
        /// A ramp from where this one is now to <paramref name="target"/>, so a fade interrupted halfway
        /// carries on from halfway rather than jumping back to its start.
        /// </summary>
        public MusicRamp Retarget(float target, float rampSeconds, MusicEase rampEase)
        {
            return new MusicRamp(Value, target, rampSeconds, rampEase);
        }
    }
}
