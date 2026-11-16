namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>Reads the load type a clip's import settings hold, and moves it to the one a track asks for.</summary>
    /// <remarks>
    /// Unity stores the load type on the clip, and a build cannot change it while playing, so a track's choice only
    /// becomes real when the importer carries it. A generated clip has no importer: only an imported asset takes one.
    /// </remarks>
    public static class MusicClipImport
    {
        /// <summary>The load type the clip's import settings hold for this editor's build target.</summary>
        public static MusicClipLoadType AppliedLoadType(AudioClip clip)
        {
            if (clip == null)
            {
                return MusicClipLoadType.Auto;
            }

            return clip.loadType switch
            {
                AudioClipLoadType.CompressedInMemory => MusicClipLoadType.CompressedInMemory,
                AudioClipLoadType.Streaming => MusicClipLoadType.Streaming,
                _ => MusicClipLoadType.DecompressOnLoad,
            };
        }

        /// <summary>Whether the clip's import settings hold the load type its track resolves to.</summary>
        public static bool Matches(MusicTrack track)
        {
            return track == null || track.Clip == null || AppliedLoadType(track.Clip) == track.ResolvedClipLoadType;
        }

        /// <summary>Writes <paramref name="loadType" /> into the clip's import settings and reimports it.</summary>
        /// <returns>False when the clip has no importer to write, or the track asks for no concrete type.</returns>
        public static bool TryApply(AudioClip clip, MusicClipLoadType loadType)
        {
            if (clip == null || loadType == MusicClipLoadType.Auto)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer))
            {
                return false;
            }

            AudioClipLoadType wanted = ToUnity(loadType);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType == wanted)
            {
                return true;
            }

            settings.loadType = wanted;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            return true;
        }

        private static AudioClipLoadType ToUnity(MusicClipLoadType loadType)
        {
            return loadType switch
            {
                MusicClipLoadType.CompressedInMemory => AudioClipLoadType.CompressedInMemory,
                MusicClipLoadType.Streaming => AudioClipLoadType.Streaming,
                _ => AudioClipLoadType.DecompressOnLoad,
            };
        }
    }
}
