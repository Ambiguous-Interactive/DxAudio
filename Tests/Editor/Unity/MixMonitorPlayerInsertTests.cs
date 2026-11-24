namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DxAudio.Editor;

    public sealed class MixMonitorPlayerInsertTests
    {
        [Test]
        public void EachStripShowsItsOwnBusEffectsInSignalOrder()
        {
            using MadeObjects made = new();
            DxAudioMixMonitorWindow window = made.Track(ScriptableObject.CreateInstance<DxAudioMixMonitorWindow>());
            SfxPlayer sfx = made.Track(new GameObject("SFX")).AddComponent<SfxPlayer>();
            MusicPlayer music = made.Track(new GameObject("Music")).AddComponent<MusicPlayer>();
            DspLimiterAsset limiter = made.Track(ScriptableObject.CreateInstance<DspLimiterAsset>());
            DspCompressorAsset compressor = made.Track(ScriptableObject.CreateInstance<DspCompressorAsset>());
            limiter.name = "Ceiling";
            compressor.name = "Leveling";
            sfx.BusChain = made.Track(DspChainAsset.Create(new DspEffectAsset[] { compressor, null, limiter, compressor }));
            music.BusChain = made.Track(DspChainAsset.Create(new DspEffectAsset[] { limiter }));
            window.SelectPlayers(sfx, music);

            GUIContent sfxLabel = window.PlayerInsertLabel(forSfx: true);
            Assert.That(sfxLabel.text, Is.EqualTo("Leveling > Ceiling > Leveling"));
            Assert.That(sfxLabel.tooltip, Is.EqualTo(sfxLabel.text));
            Assert.That(window.PlayerInsertLabel(forSfx: false).text, Is.EqualTo("Ceiling"));
        }

        [Test]
        public void PausingCaptureStillShowsChangesToTheSelectedBusChain()
        {
            using MadeObjects made = new();
            DxAudioMixMonitorWindow window = made.Track(ScriptableObject.CreateInstance<DxAudioMixMonitorWindow>());
            MusicPlayer music = made.Track(new GameObject("Music")).AddComponent<MusicPlayer>();
            DspLimiterAsset limiter = made.Track(ScriptableObject.CreateInstance<DspLimiterAsset>());
            limiter.name = "Ceiling";
            window.SelectPlayers(null, music);
            window.ToggleCapture();
            Assert.That(window.Capture.Paused, Is.True);
            Assert.That(window.PlayerInsertLabel(forSfx: true).text, Is.EqualTo("None"));
            Assert.That(window.PlayerInsertLabel(forSfx: false).text, Is.EqualTo("None"));

            music.BusChain = made.Track(DspChainAsset.Create(System.Array.Empty<DspEffectAsset>()));
            Assert.That(window.PlayerInsertLabel(forSfx: false).text, Is.EqualTo("None"));
            music.BusChain = made.Track(DspChainAsset.Create(new DspEffectAsset[] { limiter }));
            Assert.That(window.PlayerInsertLabel(forSfx: false).text, Is.EqualTo("Ceiling"));
            made.DestroyNow(limiter);
            Assert.That(window.PlayerInsertLabel(forSfx: false).text, Is.EqualTo("None"));
            made.DestroyNow(music.BusChain);
            Assert.That(window.PlayerInsertLabel(forSfx: false).text, Is.EqualTo("None"));
        }
    }
}
