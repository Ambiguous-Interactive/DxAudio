namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>Lists every sound and music library in the project and what would make its content unreachable.</summary>
    /// <remarks>
    /// The players report a bad key only when something asks for it. This window checks the whole project at
    /// once, so a duplicate key or a track with no context is visible before play. It only reads assets.
    /// </remarks>
    public sealed class DxAudioLibraryWindow : EditorWindow
    {
        private readonly List<Object> _libraries = new();
        private readonly List<List<string>> _findings = new();

        private Vector2 _scroll;

        [MenuItem("Tools/DxAudio/Validate Libraries")]
        private static void Open()
        {
            DxAudioLibraryWindow window = GetWindow<DxAudioLibraryWindow>();
            window.titleContent = new GUIContent("DxAudio Libraries");
            window.Refresh();
            window.Show();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Check Again"))
            {
                Refresh();
            }

            int problems = 0;
            for (int index = 0; index < _findings.Count; ++index)
            {
                problems += _findings[index].Count;
            }

            EditorGUILayout.LabelField($"{_libraries.Count} libraries, {problems} problems", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_libraries.Count == 0)
            {
                EditorGUILayout.HelpBox("This project has no DxAudio libraries.", MessageType.Info);
            }

            for (int index = 0; index < _libraries.Count; ++index)
            {
                EditorGUILayout.ObjectField(_libraries[index], typeof(Object), false);
                List<string> findings = _findings[index];
                if (findings.Count == 0)
                {
                    EditorGUILayout.HelpBox("No problems found.", MessageType.Info);
                    continue;
                }

                for (int finding = 0; finding < findings.Count; ++finding)
                {
                    EditorGUILayout.HelpBox(findings[finding], MessageType.Warning);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            _libraries.Clear();
            _findings.Clear();
            AddLibraries<SfxLibrary>("t:SfxLibrary", DxAudioValidation.Validate);
            AddLibraries<MusicLibrary>("t:MusicLibrary", DxAudioValidation.Validate);
            Repaint();
        }

        private void AddLibraries<T>(string filter, Func<T, List<string>> validate)
            where T : Object
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            List<string> paths = new(guids.Length);
            for (int index = 0; index < guids.Length; ++index)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guids[index]));
            }

            paths.Sort(StringComparer.Ordinal);
            for (int index = 0; index < paths.Count; ++index)
            {
                T library = AssetDatabase.LoadAssetAtPath<T>(paths[index]);
                if (library == null)
                {
                    continue;
                }

                _libraries.Add(library);
                _findings.Add(validate(library));
            }
        }
    }
}
