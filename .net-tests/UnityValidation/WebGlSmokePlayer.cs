namespace WallstopStudios.DxAudio.Validation
{
    using UnityEngine;

    /*
        The player-side half of the WebGL smoke scene (PLAN.md W-CI, spec map 01). It builds its clips, cues, chain and
        graph in code, plays one dry cue and one cue shaped through a chain plus graph, and logs structured state for
        the browser driver of the next W-CI step. The shaped cue is the probe the Web lane will measure: Unity does not
        run OnAudioFilterRead on Web, so today the shaped cue must come out identical to the dry one, which is the
        limitation W-CI exists to record. The tone fades at both ends so no click rides the play.
    */
    [AddComponentMenu("Wallstop Studios/DxAudio/Validation/Web GL Smoke Player")]
    public sealed class WebGlSmokePlayer : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const string LogPrefix = "dxaudio-web-smoke";

        private AudioClip _clip;
        private SfxCue _dryCue;
        private SfxCue _shapedCue;
        private SfxLibrary _library;

        private static AudioClip MakeTone(string name, float frequencyHz, float seconds)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * seconds));
            float[] samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; ++index)
            {
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

        private void Awake()
        {
            SfxPlayer player = GetComponent<SfxPlayer>();
            if (player == null)
            {
                Debug.Log($"{LogPrefix} {{\"event\":\"error\",\"reason\":\"missing SfxPlayer\"}}");
                return;
            }

            _clip = MakeTone("SmokeTone", 440f, 0.8f);
            DspChainAsset chain = DspChainAsset.Create(new DspEffectAsset[] { DspBitCrusherAsset.Create(bits: 1), DspDistortionAsset.Create(drive: 20f) });
            DspGraphAsset graph = DspGraphAsset.Create(
                new[]
                {
                    new DspGraphNode("in", null),
                    new DspGraphNode("branch", DspChainAsset.Create(new DspEffectAsset[] { DspBitCrusherAsset.Create(bits: 1) })),
                    new DspGraphNode("out", null),
                },
                new[] { new DspGraphEdge("in", "branch"), new DspGraphEdge("branch", "out") },
                "in",
                "out"
            );
            _dryCue = SfxCue.Create(new[] { new SfxVariant(_clip) }, key: "smoke-dry", volume: 0.8f, minimumIntervalSeconds: 0f);
            _shapedCue = SfxCue.CreateWithGraph(new[] { new SfxVariant(_clip) }, graph, key: "smoke-shaped", volume: 0.8f, minimumIntervalSeconds: 0f, chain: chain);
            _library = SfxLibrary.Create(new[] { _dryCue, _shapedCue }, voiceCount: 4, loopingVoiceLimit: 0);
            player.Library = _library;
        }

        private void Start()
        {
            SfxPlayer player = GetComponent<SfxPlayer>();
            if (player == null)
            {
                return;
            }

            bool dry = player.Play(_dryCue);
            bool shaped = player.Play(_shapedCue);
            Debug.Log($"{LogPrefix} {{\"event\":\"played\",\"dry\":{(dry ? "true" : "false")},\"shaped\":{(shaped ? "true" : "false")}}}");
            Debug.Log($"{LogPrefix} {{\"event\":\"state\",\"voices\":{player.VoiceCount},\"held\":{player.HeldVoiceCount}}}");
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(_library);
            DestroyRuntimeObject(_dryCue);
            DestroyRuntimeObject(_shapedCue);
            DestroyRuntimeObject(_clip);
        }
    }
}
