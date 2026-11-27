namespace WallstopStudios.DxAudio.Samples
{
    using UnityEngine;

    /// <summary>
    /// Drives the Basic Usage sample: plays a cue, holds a loop, starts music and ducks it.
    /// </summary>
    /// <remarks>
    /// The sample builds its clips, cues and libraries in code so it ships without audio files. A game authors the
    /// same assets with the Create menu and assigns them to the players in the inspector.
    /// </remarks>
    [AddComponentMenu("Wallstop Studios/DxAudio/Samples/Basic Usage Demo")]
    public sealed class BasicUsageDemo : MonoBehaviour
    {
        private const int SampleRate = 44100;

        [SerializeField]
        [Tooltip("The player this sample gives a library to.")]
        private SfxPlayer _sfx;

        [SerializeField]
        [Tooltip("The player this sample gives a music library to.")]
        private MusicPlayer _music;

        private AudioClip _blipClip;
        private AudioClip _humClip;
        private AudioClip _musicClip;
        private SfxCue _blipCue;
        private SfxCue _humCue;
        private SfxLibrary _sfxLibrary;
        private MusicTrack _musicTrack;
        private MusicLibrary _musicLibrary;
        private MusicDuck _duck;
        private SfxLoopHandle _humHandle;
        private MusicDuckLease _duckLease;
        private bool _loopHeld;
        private string _message = "Start the scene to hear a cue, a loop and music.";

        private static AudioClip MakeTone(string name, float frequencyHz, float seconds)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * seconds));
            float[] samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; ++index)
            {
                // Fade both ends so a one-shot does not click and a loop does not restart audibly.
                float position = index / (float)sampleCount;
                float envelope = Mathf.Min(position * 20f, (1f - position) * 20f, 1f);
                float secondsAtSample = index / (float)SampleRate;
                samples[index] = Mathf.Sin(2f * Mathf.PI * frequencyHz * secondsAtSample) * 0.5f * envelope;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
                return;
            }

            DestroyImmediate(target);
        }

        private void Start()
        {
            if (_sfx == null || _music == null)
            {
                _message = "Assign the Sfx Player and the Music Player in the inspector.";
                Debug.LogWarning(_message, this);
                return;
            }

            _blipClip = MakeTone("Blip", 880f, 0.25f);
            _humClip = MakeTone("Hum", 220f, 1f);
            _musicClip = MakeTone("Music", 330f, 4f);

            _blipCue = SfxCue.Create(new SfxVariant[] { new SfxVariant(_blipClip) }, key: "blip", volume: 0.6f);
            _humCue = SfxCue.Create(new SfxVariant[] { new SfxVariant(_humClip) }, key: "hum", volume: 0.25f, loop: true);
            _sfxLibrary = SfxLibrary.Create(new SfxCue[] { _blipCue, _humCue }, voiceCount: 8, loopingVoiceLimit: 2);
            _sfx.Library = _sfxLibrary;

            _musicTrack = MusicTrack.Create(_musicClip, context: "demo", loop: true, volumeScale: 0.4f);
            _musicLibrary = MusicLibrary.Create(new MusicTrack[] { _musicTrack }, fadeInSeconds: 1.5f, fadeOutSeconds: 0.8f);
            _music.Library = _musicLibrary;

            // Ducks lower the music while held. One holder is enough for a single dialogue or menu.
            _duck = MusicDuck.Create(MusicDuckHolders.One, volumeScale: 0.3f, lowPassCutoffHz: 1200f);

            _music.PlayContext("demo");
            _message = "The cue plays, the loop holds a voice, and music fades in.";
        }

        private void OnDestroy()
        {
            if (_music != null)
            {
                _music.Release(_duckLease);
            }

            DestroyRuntimeObject(_sfxLibrary);
            DestroyRuntimeObject(_musicLibrary);
            DestroyRuntimeObject(_blipCue);
            DestroyRuntimeObject(_humCue);
            DestroyRuntimeObject(_musicTrack);
            DestroyRuntimeObject(_duck);
            DestroyRuntimeObject(_blipClip);
            DestroyRuntimeObject(_humClip);
            DestroyRuntimeObject(_musicClip);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20f, 20f, 360f, 260f), GUI.skin.box);
            GUILayout.Label("DxAudio Basic Usage");

            if (_sfx == null || _music == null)
            {
                // Start explains the missing reference; drawing here keeps the hint visible.
                GUILayout.Label(_message);
                GUILayout.EndArea();
                return;
            }

            if (GUILayout.Button("Play cue"))
            {
                PlayCue();
            }

            if (GUILayout.Button(_loopHeld ? "Stop loop" : "Start loop"))
            {
                ToggleLoop();
            }

            if (GUILayout.Button(_music.IsDucked(_duck) ? "Release duck" : "Duck music"))
            {
                ToggleDuck();
            }

            GUILayout.Label(_message);
            GUILayout.Label($"Music playing: {_music.IsPlaying}");
            GUILayout.EndArea();
        }

        private void PlayCue()
        {
            _message = _sfx.Play(_blipCue) ? "Played the cue." : "The cue was dropped.";
        }

        private void ToggleLoop()
        {
            if (_loopHeld)
            {
                _sfx.StopLoop(_humHandle);
                _loopHeld = false;
                _message = "Stopped the loop.";
                return;
            }

            _loopHeld = _sfx.TryPlayLooping(_humCue, out _humHandle);
            _message = _loopHeld ? "Held a looping voice." : "No voice was free for the loop.";
        }

        private void ToggleDuck()
        {
            if (_music.IsDucked(_duck))
            {
                _music.Release(_duckLease);
                _message = "Released the duck. The music fades back.";
                return;
            }

            _duckLease = _music.Duck(_duck, this);
            _message = "Held the duck. The music lowers and muffles.";
        }
    }
}
