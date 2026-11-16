namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>Reads the load type a clip's import settings hold, and moves it to the one a track asks for.</summary>
    /// <remarks>
    /// Unity stores the load type on the clip, and a build cannot change it while playing, so a track's choice only
    /// becomes real when the importer carries it. The clip holds one value, so every track and cue that shares the
    /// clip takes the same one. A generated clip has no importer, and a per-platform override wins over the default
    /// settings, so a write that cannot change the effective type reports false rather than claiming success.
    /// </remarks>
    public static class MusicClipImport
    {
        /// <summary>Reads the load type the clip's import settings hold for this editor's build target.</summary>
        public static bool TryGetAppliedLoadType(AudioClip clip, out MusicClipLoadType loadType)
        {
            if (clip == null)
            {
                loadType = MusicClipLoadType.Auto;
                return false;
            }

            loadType = clip.loadType switch
            {
                AudioClipLoadType.CompressedInMemory => MusicClipLoadType.CompressedInMemory,
                AudioClipLoadType.Streaming => MusicClipLoadType.Streaming,
                _ => MusicClipLoadType.DecompressOnLoad,
            };
            return true;
        }

        /// <summary>Whether the clip's effective load type is the one its track resolves to.</summary>
        public static bool Matches(MusicTrack track)
        {
            if (track == null || track.Clip == null)
            {
                return false;
            }

            return TryGetAppliedLoadType(track.Clip, out MusicClipLoadType applied)
                && applied == track.ResolvedClipLoadType;
        }

        /// <summary>Whether the clip has an importer this editor can write.</summary>
        public static bool CanApply(AudioClip clip)
        {
            return Importer(EditablePath(clip)) != null;
        }

        /// <summary>Writes <paramref name="loadType" /> into the clip's import settings and reimports it.</summary>
        /// <returns>
        /// False when the clip has no editable importer, when the track asks for no concrete type, or when the
        /// effective type did not change because a per-platform override controls it.
        /// </returns>
        public static bool TryApply(AudioClip clip, MusicClipLoadType loadType)
        {
            if (loadType == MusicClipLoadType.Auto)
            {
                return false;
            }

            string path = EditablePath(clip);
            AudioImporter importer = Importer(path);
            if (importer == null)
            {
                return false;
            }

            AudioClipLoadType wanted = ToUnity(loadType);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType != wanted)
            {
                settings.loadType = wanted;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }

            return TryGetAppliedLoadType(AssetDatabase.LoadAssetAtPath<AudioClip>(path), out MusicClipLoadType applied)
                && applied == loadType;
        }

        private static string EditablePath(AudioClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return AssetDatabase.IsOpenForEdit(path, StatusQueryOptions.UseCachedIfPossible) ? path : null;
        }

        private static AudioImporter Importer(string path)
        {
            return path == null ? null : AssetImporter.GetAtPath(path) as AudioImporter;
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
