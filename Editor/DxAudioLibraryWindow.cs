namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using static DxAudioValidation;
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
            List<List<Finding>> findings = new();
            Collect(libraries, findings);
            content.Add(BuildProblemReport(libraries, findings));
            return content;
        }

        /// <summary>The report the window shows for <paramref name="libraries" /> and their findings.</summary>
        public static VisualElement BuildReport(IReadOnlyList<Object> libraries, IReadOnlyList<List<string>> findings)
        {
            return BuildReport(
                libraries,
                Summary(libraries.Count, CountLegacyFindings(findings)),
                (scroll, index) =>
                {
                    List<string> entries = findings[index];
                    if (entries.Count == 0)
                    {
                        scroll.Add(new HelpBox("No problems found.", HelpBoxMessageType.Info));
                    }

                    foreach (string finding in entries)
                    {
                        scroll.Add(new HelpBox(finding, HelpBoxMessageType.Warning));
                    }
                }
            );
        }

        internal static VisualElement BuildProblemReport(IReadOnlyList<Object> libraries, IReadOnlyList<List<Finding>> findings)
        {
            VisualElement report = BuildReport(
                libraries,
                string.Empty,
                (scroll, index) => scroll.Add(new DxAudioInspectorUi.ProblemList(findings[index], "No active problems.", findingIndex => OpenAsset(findings[index][findingIndex])))
            );
            Label summary = report.Q<Label>();
            HelpBox failure = new() { name = "DxAudioValidationFailed", messageType = HelpBoxMessageType.Error };
            report.Insert(1, failure);

            void UpdateStatus()
            {
                DxAudioProblemSettings settings = DxAudioProblemSettings.instance;
                int visible = 0;
                foreach (List<Finding> group in findings)
                {
                    visible += group.FindAll(settings.IsVisible).Count;
                }

                summary.text = Summary(libraries.Count, visible);
                int errors = BlockingErrors(findings).Count;
                failure.style.display = errors == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                failure.text =
                    errors == 1 ? "Validation failed. One error needs a fix or a shared silence." : $"Validation failed. {errors} errors need a fix or a shared silence.";
            }

            report.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                DxAudioProblemSettings.Changed += UpdateStatus;
                UpdateStatus();
            });
            report.RegisterCallback<DetachFromPanelEvent>(_ => DxAudioProblemSettings.Changed -= UpdateStatus);
            UpdateStatus();
            return report;
        }

        internal static List<Finding> BlockingErrors(IReadOnlyList<List<Finding>> findings)
        {
            List<Finding> errors = new();
            DxAudioProblemSettings settings = DxAudioProblemSettings.instance;
            for (int index = 0; index < findings.Count; ++index)
            {
                errors.AddRange(findings[index].FindAll(settings.BlocksValidation));
            }

            return errors;
        }

        internal static List<Finding> FindProjectErrors()
        {
            List<Object> libraries = new();
            List<List<Finding>> findings = new();
            Collect(libraries, findings);
            return BlockingErrors(findings);
        }

        private static Action OpenAsset(Finding finding)
        {
            return finding.Asset == null ? null : () => DxAudioAuthoringWindow.Open(finding.Asset);
        }

        private static int CountLegacyFindings(IReadOnlyList<List<string>> findings)
        {
            int count = 0;
            foreach (List<string> group in findings)
            {
                count += group.Count;
            }

            return count;
        }

        private static VisualElement BuildReport(IReadOnlyList<Object> libraries, string heading, Action<VisualElement, int> draw)
        {
            VisualElement report = new VisualElement();
            report.style.flexGrow = 1f;
            Label summary = new Label(heading);
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
                Object library = libraries[index];
                ObjectField field = new ObjectField
                {
                    objectType = typeof(Object),
                    allowSceneObjects = false,
                    value = library,
                };
                field.SetEnabled(false);
                field.style.flexGrow = 1;
                VisualElement row = DxAudioInspectorUi.Row();
                row.Add(field);
                if (library != null)
                {
                    row.Add(new Button(() => DxAudioAuthoringWindow.Open(library)) { text = "Open Library" });
                }

                scroll.Add(row);
                draw(scroll, index);
            }

            report.Add(scroll);
            return report;
        }

        private static string Summary(int libraries, int problems)
        {
            string libraryWord = libraries == 1 ? "library" : "libraries";
            string problemWord = problems == 1 ? "problem" : "problems";
            return $"{libraries} {libraryWord}, {problems} {problemWord}";
        }

        [MenuItem("Tools/Wallstop Studios/DxAudio/Validate Libraries")]
        private static void Open()
        {
            DxAudioLibraryWindow window = GetWindow<DxAudioLibraryWindow>();
            window.titleContent = new GUIContent("DxAudio Libraries");
            // An open window keeps its last report, so the menu item validates again instead of showing it.
            window.Refresh();
            window.Show();
        }

        private static void Collect(List<Object> libraries, List<List<Finding>> findings)
        {
            AddLibraries<SfxLibrary>("t:SfxLibrary", libraries, findings, DxAudioValidation.Find);
            AddLibraries<MusicLibrary>("t:MusicLibrary", libraries, findings, DxAudioValidation.Find);
        }

        private static void AddLibraries<T>(string filter, List<Object> libraries, List<List<Finding>> findings, Func<T, List<Finding>> validate)
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
