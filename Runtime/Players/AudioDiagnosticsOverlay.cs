namespace WallstopStudios.DxAudio
{
    using System;
    using System.Text;
    using UnityEngine;

    /// <summary>An on-screen report of what the players are doing, for use while debugging a game.</summary>
    /// <remarks>
    /// The overlay lists every busy voice with its priority and loop lease, every duck and who holds it, and
    /// how many cues were dropped by reason. Sampling runs only while <see cref="Enabled"/> is set, so an
    /// overlay that is present but switched off allocates nothing per frame. Drawing adds its own cost on top
    /// while the overlay is open.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("DxAudio/Audio Diagnostics")]
    public sealed class AudioDiagnosticsOverlay : MonoBehaviour
    {
        private const int DuckCapacity = 32;
        private const float OverlayX = 10f;
        private const float OverlayY = 10f;
        private const float OverlayWidth = 620f;

        [SerializeField]
        [Tooltip("The sound-effects player to report on.")]
        private SfxPlayer _sfx;

        [SerializeField]
        [Tooltip("The music player to report on.")]
        private MusicPlayer _music;

        [SerializeField]
        [Tooltip("Draws the overlay. Sampling runs only while this is set.")]
        private bool _enabled;

        private readonly StringBuilder _builder = new();
        private IMusicDuckReason[] _duckReasons;
        private object[] _duckHolders;
        private long[] _duckIds;
        private string _text = string.Empty;

        /// <summary>Whether the overlay samples and draws. Switching it off stops both.</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;
                if (value)
                {
                    PrepareBuffers();
                }
                else
                {
                    _text = string.Empty;
                }
            }
        }

        public SfxPlayer Sfx
        {
            get => _sfx;
            set => _sfx = value;
        }

        public MusicPlayer Music
        {
            get => _music;
            set => _music = value;
        }

        /// <summary>The last sampled report, or empty while the overlay is off.</summary>
        public string Text => _text;

        private void OnEnable()
        {
            if (_enabled)
            {
                PrepareBuffers();
            }
        }

        private void OnDisable()
        {
            _text = string.Empty;
        }

        private void Update()
        {
            Sample();
        }

        /// <summary>Rebuilds <see cref="Text"/>. Does nothing, and allocates nothing, while the overlay is off.</summary>
        public void Sample()
        {
            if (!_enabled)
            {
                return;
            }

            PrepareBuffers();
            _builder.Clear();
            AppendSfx(_builder);
            AppendMusic(_builder);
            _text = _builder.ToString();
        }

        private void OnGUI()
        {
            if (!_enabled || _text.Length == 0)
            {
                return;
            }

            Rect area = new(OverlayX, OverlayY, OverlayWidth, Math.Max(0f, Screen.height - (2f * OverlayY)));
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label(_text);
            GUILayout.EndArea();
        }

        private void AppendSfx(StringBuilder text)
        {
            SfxPlayer player = _sfx;
            if (player == null)
            {
                _ = text.AppendLine("Sfx: no player assigned.");
                return;
            }

            int busy = 0;
            for (int index = 0; index < player.VoiceCount; index++)
            {
                if (
                    player.TryGetVoiceState(index, out SfxVoiceState state, out _)
                    && state.isUsable
                    && (state.isPlaying || state.isHeld)
                )
                {
                    ++busy;
                }
            }

            _ = text.Append("Sfx: ")
                .Append(player.VoiceCount)
                .Append(" voices, ")
                .Append(busy)
                .Append(" busy, ")
                .Append(player.HeldVoiceCount)
                .AppendLine(" loop(s) held");

            for (int index = 0; index < player.VoiceCount; index++)
            {
                if (!player.TryGetVoiceState(index, out SfxVoiceState state, out int lease))
                {
                    continue;
                }

                if (!state.isUsable)
                {
                    _ = text.Append("  v").Append(index).AppendLine(": destroyed");
                    continue;
                }

                if (!state.isPlaying && !state.isHeld)
                {
                    continue;
                }

                _ = text.Append("  v").Append(index).Append(": ").Append(state.priority);
                if (state.isHeld)
                {
                    _ = text.Append(" loop lease ").Append(lease);
                }

                if (state.isPlaying)
                {
                    float age = Math.Max(0f, Time.unscaledTime - state.startedAtUnscaledTime);
                    _ = text.Append(" playing ").Append(age.ToString("0.0")).Append('s');
                }
                else
                {
                    _ = text.Append(" silent");
                }

                _ = text.AppendLine();
            }

            SfxDropCounts counts = player.DropCounts;
            _ = text.Append("Dropped: ").Append(counts.Total);
            if (0 < counts.Total)
            {
                _ = text.Append(" (");
                bool first = true;
                AppendDropCount(text, counts, SfxDropReason.NotPlayable, ref first);
                AppendDropCount(text, counts, SfxDropReason.Throttled, ref first);
                AppendDropCount(text, counts, SfxDropReason.NoVoice, ref first);
                AppendDropCount(text, counts, SfxDropReason.LoopLimit, ref first);
                AppendDropCount(text, counts, SfxDropReason.WrongPlayMethod, ref first);
                _ = text.Append(')');
            }

            _ = text.AppendLine();
        }

        private static void AppendDropCount(
            StringBuilder text,
            SfxDropCounts counts,
            SfxDropReason reason,
            ref bool first
        )
        {
            int count = counts[reason];
            if (count < 1)
            {
                return;
            }

            if (first)
            {
                first = false;
            }
            else
            {
                _ = text.Append(", ");
            }

            _ = text.Append(reason.ToString()).Append(' ').Append(count);
        }

        private void AppendMusic(StringBuilder text)
        {
            MusicPlayer player = _music;
            if (player == null)
            {
                _ = text.AppendLine("Music: no player assigned.");
                return;
            }

            MusicTrack track = player.Current;
            _ = text.Append("Music: ");
            if (track == null)
            {
                _ = text.Append("none");
            }
            else
            {
                string context = track.Context;
                _ = text.Append(string.IsNullOrEmpty(context) ? track.name : context);
                _ = text.Append(player.IsPlaying ? " playing" : " silent");
            }

            if (player.IsOverridden)
            {
                _ = text.Append(" (override)");
            }

            _ = text.AppendLine();
            _ = text.Append("  applied ").AppendLine(player.Attenuation.ToString());
            _ = text.Append("  target  ").AppendLine(player.TargetAttenuation.ToString());

            int count = player.CopyDuckHolds(_duckReasons, _duckHolders, _duckIds);
            _ = text.Append("Ducks: ").Append(count).AppendLine();
            int shown = Math.Min(count, _duckReasons.Length);
            for (int index = 0; index < shown; index++)
            {
                IMusicDuckReason reason = _duckReasons[index];
                _ = text.Append("  ");
                _ = text.Append(reason != null ? reason.ToString() : "a null reason");
                _ = text.Append(" (")
                    .Append(reason != null ? reason.Holders.ToString() : "Unknown")
                    .Append(") #")
                    .Append(_duckIds[index]);

                object holder = _duckHolders[index];
                if (holder != null)
                {
                    _ = text.Append(" held by ")
                        .Append(UnityObjects.IsDestroyed(holder) ? "a destroyed object" : holder.ToString());
                }

                _ = text.AppendLine();
            }

            if (shown < count)
            {
                _ = text.Append("  +").Append(count - shown).AppendLine(" more");
            }
        }

        private void PrepareBuffers()
        {
            if (_duckReasons != null)
            {
                return;
            }

            _duckReasons = new IMusicDuckReason[DuckCapacity];
            _duckHolders = new object[DuckCapacity];
            _duckIds = new long[DuckCapacity];
        }
    }
}
