namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>Lists every sound and music library in the project and what would make its content unreachable.</summary>
    /// <remarks>
    /// The players report a bad key only when something asks for it. This window checks the whole project at
    /// once, so a duplicate key or a track with no context is visible before play. It only reads assets.
    /// </remarks>
    public sealed class DxAudioLibraryWindow : EditorWindow
    {
        [MenuItem("Tools/DxAudio/Validate Libraries")]
        private static void Open()
        {
            DxAudioLibraryWindow window = GetWindow<DxAudioLibraryWindow>();
            window.titleContent = new GUIContent("DxAudio Libraries");
            window.Show();
        }

        public void CreateGUI()
        {
            Rebuild();
        }

        /// <summary>The report the window shows for <paramref name="libraries" /> and their findings.</summary>
        public static VisualElement BuildReport(IReadOnlyList<Object> libraries, IReadOnlyList<List<string>> findings)
        {
            VisualElement report = new VisualElement();
            report.style.flexGrow = 1f;
            int problemCount = 0;
            for (int index = 0; index < findings.Count; ++index)
            {
                problemCount += findings[index].Count;
            }

            string libraryWord = libraries.Count == 1 ? "library" : "libraries";
            string problemWord = problemCount == 1 ? "problem" : "problems";
            Label summary = new Label($"{libraries.Count} {libraryWord}, {problemCount} {problemWord}");
            summary.style.unityFontStyleAndWeight = FontStyle.Bold;
            report.Add(summary);

            if (libraries.Count == 0)
            {
                report.Add(new HelpBox("This project has no DxAudio libraries.", HelpBoxMessageType.Info));
                return report;
            }

            ScrollView scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            for (int index = 0; index < libraries.Count; ++index)
            {
                ObjectField field = new ObjectField
                {
                    objectType = typeof(Object),
                    allowSceneObjects = false,
                    value = libraries[index],
                };
                field.SetEnabled(false);
                scroll.Add(field);

                List<string> findingsForLibrary = findings[index];
                if (findingsForLibrary.Count == 0)
                {
                    scroll.Add(new HelpBox("No problems found.", HelpBoxMessageType.Info));
                    continue;
                }

                for (int finding = 0; finding < findingsForLibrary.Count; ++finding)
                {
                    scroll.Add(new HelpBox(findingsForLibrary[finding], HelpBoxMessageType.Warning));
                }
            }

            report.Add(scroll);
            return report;
        }

        private void Rebuild()
        {
            rootVisualElement.Clear();
            List<Object> libraries = new();
            List<List<string>> findings = new();
            Collect(libraries, findings);
            rootVisualElement.Add(new Button(Rebuild) { text = "Check Again" });
            rootVisualElement.Add(BuildReport(libraries, findings));
        }

        private static void Collect(List<Object> libraries, List<List<string>> findings)
        {
            AddLibraries<SfxLibrary>("t:SfxLibrary", libraries, findings, DxAudioValidation.Validate);
            AddLibraries<MusicLibrary>("t:MusicLibrary", libraries, findings, DxAudioValidation.Validate);
        }

        private static void AddLibraries<T>(string filter, List<Object> libraries, List<List<string>> findings, Func<T, List<string>> validate)
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
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(paths[index]);
                for (int asset = 0; asset < assets.Length; ++asset)
                {
                    if (assets[asset] is T library && library != null)
                    {
                        libraries.Add(library);
                        findings.Add(validate(library));
                    }
                }
            }
        }
    }
}
