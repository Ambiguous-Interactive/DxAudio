namespace WallstopStudios.DxAudio.Validation
{
    using System;
    using System.Collections;
    using UnityEngine;

    /*
        The player-side half of the WebGL smoke scene (PLAN.md W-CI, spec map 01). It builds its clips, cues, chain and
        graph in code, plays one dry cue and one cue shaped through a chain plus graph, and logs structured state for
        the browser driver of the next W-CI step. The cues play in sequence with a silent gap between them, because
        the browser shim attributes every audio block to a cue by level: the dry clip is authored at amplitude 0.5 and
        the shaped clip at amplitude 0.25, so an inert Web output measures a shaped-to-dry ratio of exactly one half.
        That ratio is the red measurement of W-CI: Unity does not run OnAudioFilterRead on Web, so today the shaped
        cue must come out at its unshaped level, which is the limitation W-CI exists to record. If the measured ratio
        leaves the inert band, the chains actually ran on Web and the W-CI record must be updated. The tone fades at
        both ends so no click rides the play.
    */
    [AddComponentMenu("Wallstop Studios/DxAudio/Validation/Web GL Smoke Player")]
    public sealed class WebGlSmokePlayer : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const float GapSeconds = 1.2f;
        private const string LogPrefix = "dxaudio-web-smoke";

        private AudioClip _clip;
        private AudioClip _shapedClip;
        private SfxCue _dryCue;
        private SfxCue _shapedCue;
        private SfxLibrary _library;

        private static AudioClip MakeTone(string name, float frequencyHz, float amplitude, float seconds)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * seconds));
            float[] samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; ++index)
            {
                float position = index / (float)sampleCount;
                float envelope = Mathf.Min(position * 20f, (1f - position) * 20f, 1f);
                float secondsAtSample = index / (float)SampleRate;
                samples[index] = Mathf.Sin(2f * Mathf.PI * frequencyHz * secondsAtSample) * amplitude * envelope;
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

            _clip = MakeTone("SmokeTone", 440f, 0.5f, 0.8f);
            _shapedClip = MakeTone("SmokeToneShaped", 440f, 0.25f, 0.8f);
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
            _shapedCue = SfxCue.CreateWithGraph(new[] { new SfxVariant(_shapedClip) }, graph, key: "smoke-shaped", volume: 0.8f, minimumIntervalSeconds: 0f, chain: chain);
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

            StartCoroutine(PlaySequence(player));
        }

        private IEnumerator PlaySequence(SfxPlayer player)
        {
            bool dry = player.Play(_dryCue);
            Debug.Log($"{LogPrefix} {{\"event\":\"played\",\"cue\":\"dry\",\"ok\":{(dry ? "true" : "false")}}}");
            yield return new WaitForSeconds(GapSeconds);
            bool shaped = player.Play(_shapedCue);
            Debug.Log($"{LogPrefix} {{\"event\":\"played\",\"cue\":\"shaped\",\"ok\":{(shaped ? "true" : "false")}}}");
            yield return new WaitForSeconds(GapSeconds);
            Debug.Log($"{LogPrefix} {{\"event\":\"done\",\"voices\":{player.VoiceCount},\"held\":{player.HeldVoiceCount}}}");
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(_library);
            DestroyRuntimeObject(_dryCue);
            DestroyRuntimeObject(_shapedCue);
            DestroyRuntimeObject(_clip);
            DestroyRuntimeObject(_shapedClip);
        }
    }
}
