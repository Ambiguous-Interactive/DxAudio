namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using static DxAudioValidation;

    [FilePath("ProjectSettings/DxAudioProblems.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class DxAudioProblemSettings : ScriptableSingleton<DxAudioProblemSettings>
    {
        internal const string SettingsPath = "Project/Wallstop Studios/DxAudio/Problems";

        internal static event Action Changed;

        private static readonly string[] SeverityLabels = { "Error", "Warn", "Info", "Off" };

        [SerializeField]
        private List<Override> _overrides = new();

        [SerializeField]
        private List<Silence> _silences = new();

        internal static bool TryAssetId(Finding finding, out string guid, out long localId)
        {
            guid = string.Empty;
            localId = 0L;
            return finding.Asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(finding.Asset, out guid, out localId);
        }

        internal static bool IsPersonalSilence(Rule rule) => EditorPrefs.GetBool(PersonalKey(rule));

        internal static void SetPersonalSilence(Rule rule, bool hidden, bool notify = true)
        {
            string key = PersonalKey(rule);
            if (hidden)
            {
                EditorPrefs.SetBool(key, true);
            }
            else
            {
                EditorPrefs.DeleteKey(key);
            }

            if (notify)
            {
                Changed?.Invoke();
            }
        }

        private static string PersonalKey(Rule rule) => $"WallstopStudios.DxAudio.Problems.{Application.dataPath}.{Id(rule)}";

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project) { label = "Problems", guiHandler = _ => instance.DrawSettings() };
        }

        internal Severity SeverityFor(Rule rule)
        {
            foreach (Override entry in _overrides)
            {
                if (entry.rule == rule && (int)Severity.Error <= (int)entry.severity && (int)entry.severity <= (int)Severity.Off)
                {
                    return entry.severity;
                }
            }

            return DefaultSeverity(rule);
        }

        internal void SetSeverity(Rule rule, Severity severity)
        {
            _overrides.RemoveAll(entry => entry.rule == rule);
            if (severity != DefaultSeverity(rule))
            {
                _overrides.Add(new Override { rule = rule, severity = severity });
            }
        }

        internal bool BlocksValidation(Finding finding) => SeverityFor(finding.Rule) == Severity.Error && !IsSharedSilence(finding);

        internal bool IsVisible(Finding finding) => SeverityFor(finding.Rule) != Severity.Off && !IsSharedSilence(finding) && !IsPersonalSilence(finding.Rule);

        internal bool IsSharedSilence(Finding finding) =>
            TryAssetId(finding, out string guid, out long localId)
            && _silences.Exists(entry => entry.Matches(finding.Rule, guid, localId) && !string.IsNullOrWhiteSpace(entry.reason));

        internal bool TrySilence(Finding finding, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason) || !TryAssetId(finding, out string guid, out long localId))
            {
                return false;
            }

            RemoveSilence(finding.Rule, guid, localId);
            _silences.Add(
                new Silence
                {
                    rule = finding.Rule,
                    guid = guid,
                    localId = localId,
                    reason = reason.Trim(),
                }
            );
            return true;
        }

        internal void RemoveSilence(Rule rule, string guid, long localId) => _silences.RemoveAll(entry => entry.Matches(rule, guid, localId));

        internal void Persist()
        {
            Save(true);
            Changed?.Invoke();
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("DxAudio Problems", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Shared settings are saved in ProjectSettings/DxAudioProblems.asset. Personal silences apply only to this editor user.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Rule", EditorStyles.boldLabel, GUILayout.Width(58f));
            GUILayout.Label("What it catches", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label("Severity", EditorStyles.boldLabel, GUILayout.Width(176f));
            GUILayout.Label("Silenced", EditorStyles.boldLabel, GUILayout.Width(65f));
            EditorGUILayout.EndHorizontal();
            foreach (Rule rule in Rules)
            {
                if ((int)rule != 0)
                {
                    DrawRule(rule);
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Reset severity defaults"))
            {
                _overrides.Clear();
                Persist();
            }

            DrawSilences();
        }

        private void DrawRule(Rule rule)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(Id(rule), GUILayout.Width(58f));
            GUILayout.Label(Description(rule), EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(true));
            Severity severity = SeverityFor(rule);
            Severity selected = (Severity)(1 + GUILayout.Toolbar((int)severity - 1, SeverityLabels, GUILayout.Width(176f)));
            if (selected != severity)
            {
                SetSeverity(rule, selected);
                Persist();
            }

            int count = _silences.FindAll(entry => entry.rule == rule && !string.IsNullOrWhiteSpace(entry.reason)).Count;
            GUILayout.Label(count.ToString(), GUILayout.Width(65f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSilences()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shared silences", EditorStyles.boldLabel);
            for (int index = 0; index < _silences.Count; ++index)
            {
                Silence entry = _silences[index];
                string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{Id(entry.rule)}  {(string.IsNullOrEmpty(path) ? entry.guid : path)} ({entry.localId})\n{entry.reason}", EditorStyles.wordWrappedLabel);
                bool remove = GUILayout.Button("Restore", GUILayout.Width(70f));
                EditorGUILayout.EndHorizontal();
                if (remove)
                {
                    _silences.RemoveAt(index);
                    Persist();
                    break;
                }
            }

            EditorGUILayout.LabelField("Personal silences", EditorStyles.boldLabel);
            foreach (Rule rule in Rules)
            {
                if ((int)rule != 0 && IsPersonalSilence(rule) && GUILayout.Button($"Show {Id(rule)} again"))
                {
                    SetPersonalSilence(rule, false);
                }
            }
        }

        [Serializable]
        private struct Override
        {
            public Rule rule;
            public Severity severity;
        }

        [Serializable]
        private struct Silence
        {
            public Rule rule;
            public string guid;
            public long localId;
            public string reason;

            public bool Matches(Rule targetRule, string targetGuid, long targetLocalId) => rule == targetRule && guid == targetGuid && localId == targetLocalId;
        }
    }
}
