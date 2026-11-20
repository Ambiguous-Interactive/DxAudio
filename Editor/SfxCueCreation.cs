namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    internal static class SfxCueCreation
    {
        private const string MenuPath = "Assets/Wallstop Studios/DxAudio/Create Cue from Selected Clips";

        internal static SfxCue Build(IReadOnlyList<AudioClip> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return null;
            }

            List<AudioClip> ordered = new(clips.Count);
            HashSet<AudioClip> seen = new();
            for (int index = 0; index < clips.Count; ++index)
            {
                AudioClip clip = clips[index];
                if (clip != null && seen.Add(clip))
                {
                    ordered.Add(clip);
                }
            }
            if (ordered.Count == 0)
            {
                return null;
            }

            ordered.Sort(CompareClips);
            float[] attenuation = SfxCueClipDefaults.AttenuationsFor(ordered);
            SfxVariant[] takes = new SfxVariant[ordered.Count];
            for (int index = 0, takesLength = takes.Length; index < takesLength; ++index)
            {
                takes[index] = new SfxVariant(ordered[index], 0f, attenuation[index]);
            }

            SfxVariation variation = new(usePitchCents: true, 0f, 0f, 0f, 0f) { UseShuffle = 1 < takes.Length };
            return SfxCue.CreateWithVariation(takes, variation);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanCreateFromSelection()
        {
            foreach (Object item in Selection.objects)
            {
                if (IsProjectClip(item))
                {
                    return true;
                }
            }
            return false;
        }

        [MenuItem(MenuPath)]
        private static void CreateFromSelection()
        {
            List<AudioClip> selected = new();
            foreach (Object item in Selection.objects)
            {
                if (IsProjectClip(item))
                {
                    selected.Add((AudioClip)item);
                }
            }
            SfxCue cue = Build(selected);
            if (cue == null)
            {
                return;
            }

            try
            {
                string source = AssetDatabase.GetAssetPath(selected[0]);
                int separator = source?.LastIndexOf('/') ?? -1;
                string folder = 0 <= separator ? source.Substring(0, separator) : null;
                if (
                    string.IsNullOrEmpty(folder)
                    || (!string.Equals(folder, "Assets", StringComparison.Ordinal) && !folder.StartsWith("Assets/", StringComparison.Ordinal))
                    || !AssetDatabase.IsValidFolder(folder)
                    || !AssetDatabase.IsOpenForEdit(folder)
                )
                {
                    folder = "Assets";
                }

                string name = Path.GetFileNameWithoutExtension(source);
                name = string.IsNullOrEmpty(name) ? "Sfx Cue" : name + " Cue";
                string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset");
                cue.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(cue, path);
                if (!AssetDatabase.Contains(cue))
                {
                    return;
                }

                Selection.activeObject = cue;
                EditorGUIUtility.PingObject(cue);
                DxAudioAuthoringWindow.Open(cue);
            }
            finally
            {
                if (cue != null && !AssetDatabase.Contains(cue))
                {
                    Object.DestroyImmediate(cue);
                }
            }
        }

        private static bool IsProjectClip(Object item)
        {
            return item is AudioClip && item != null && AssetDatabase.Contains(item);
        }

        private static int CompareClips(AudioClip left, AudioClip right)
        {
            int byPath = StringComparer.Ordinal.Compare(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right));
            if (byPath != 0)
            {
                return byPath;
            }

            int byName = StringComparer.Ordinal.Compare(left.name, right.name);
            if (byName != 0)
            {
                return byName;
            }

            long leftId = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(left, out _, out long leftLocalId) ? leftLocalId : 0L;
            long rightId = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(right, out _, out long rightLocalId) ? rightLocalId : 0L;
            return leftId.CompareTo(rightId);
        }
    }
}
