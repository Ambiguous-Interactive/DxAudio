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
        /// <summary>The whole window tree: the recheck button and the current report.</summary>
        public static VisualElement BuildWindowContent(Action checkAgain)
        {
            VisualElement content = new VisualElement();
            content.style.flexGrow = 1f;
            content.Add(new Button(checkAgain) { text = "Check Again" });
            List<Object> libraries = new();
            List<List<string>> findings = new();
            Collect(libraries, findings);
            content.Add(BuildReport(libraries, findings));
            return content;
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
                field.style.flexGrow = 1;
                VisualElement row = DxAudioInspectorUi.Row();
                row.Add(field);
                Object library = libraries[index];
                if (library != null)
                {
                    row.Add(new Button(() => DxAudioAuthoringWindow.Open(library)) { text = "Open Library" });
                }

                scroll.Add(row);

                List<string> findingsForLibrary = findings[index];
                if (findingsForLibrary.Count == 0)
                {
                    scroll.Add(new HelpBox("No problems found.", HelpBoxMessageType.Info));
                    continue;
                }

                foreach (string finding in findingsForLibrary)
                {
                    scroll.Add(new HelpBox(finding, HelpBoxMessageType.Warning));
                }
            }

            report.Add(scroll);
            return report;
        }

        [MenuItem("Tools/DxAudio/Validate Libraries")]
        private static void Open()
        {
            DxAudioLibraryWindow window = GetWindow<DxAudioLibraryWindow>();
            window.titleContent = new GUIContent("DxAudio Libraries");
            // An open window keeps its last report, so the menu item validates again instead of showing it.
            window.Refresh();
            window.Show();
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
            foreach (string guid in guids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            paths.Sort(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (Object asset in assets)
                {
                    if (asset is T library && library != null)
                    {
                        libraries.Add(library);
                        findings.Add(validate(library));
                    }
                }
            }
        }

        public void CreateGUI()
        {
            Refresh();
        }

        private void Refresh()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(BuildWindowContent(Refresh));
        }
    }
}
