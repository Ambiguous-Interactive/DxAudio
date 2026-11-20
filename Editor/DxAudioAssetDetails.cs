namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>Adapts the asset inspectors and library membership to the authoring workspace.</summary>
    internal sealed class DxAudioAssetDetails : VisualElement
    {
        internal readonly struct Member
        {
            public readonly Object Asset;
            public readonly int Slot;
            public readonly string Lookup;

            public Member(Object asset, int slot, string lookup)
            {
                Asset = asset;
                Slot = slot;
                Lookup = lookup;
            }

            public string Label =>
                Asset == null ? $"Slot {Slot} / Missing asset"
                : string.IsNullOrWhiteSpace(Lookup) ? $"Slot {Slot} / {Asset.name}"
                : $"Slot {Slot} / {Asset.name} / {Lookup}";
        }

        internal static bool Supports(Object target)
        {
            return target != null && target is SfxCue or MusicTrack or MusicDuck or SfxLibrary or MusicLibrary or AudioMixBus or DspEffectAsset;
        }

        internal static VisualElement Build(Object target, Action<Object> open)
        {
            VisualElement root = new DxAudioAssetDetails();
            if (!Supports(target))
            {
                return root;
            }

            if (target is SfxLibrary or MusicLibrary)
            {
                root.Add(BuildLibrary(target, open));
            }

            if (target is SfxCue or MusicTrack or AudioMixBus)
            {
                VisualElement links = DxAudioInspectorUi.Row();
                void RefreshLinks()
                {
                    links.Clear();
                    switch (target)
                    {
                        case SfxCue cue when cue != null:
                            AddLink(links, "Open Followed Cue", cue.FollowsTakeOf == cue ? null : cue.FollowsTakeOf, open);
                            break;
                        case MusicTrack track when track != null:
                            AddLink(links, "Open Chain", track.Chain, open);
                            AddLink(links, "Open Graph", track.Graph, open);
                            break;
                        case AudioMixBus bus when bus != null:
                            AddLink(links, "Open Mix Chain", bus.Chain, open);
                            break;
                    }
                }

                RefreshLinks();
                links.TrackSerializedObjectValue(new SerializedObject(target), _ => RefreshLinks());
                root.Add(links);
            }

            VisualElement inspectorHost = new();
            root.Add(inspectorHost);
            UnityEditor.Editor editor = null;
            inspectorHost.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (target != null && editor == null)
                {
                    editor = UnityEditor.Editor.CreateEditor(target);
                    inspectorHost.Add(new InspectorElement(editor));
                }
            });
            inspectorHost.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                inspectorHost.Unbind();
                inspectorHost.Clear();
                if (editor != null)
                {
                    Object.DestroyImmediate(editor);
                    editor = null;
                }
            });
            return root;
        }

        internal static void AddWorkspaceButton(VisualElement root, Object target)
        {
            Button button = new(() => DxAudioAuthoringWindow.Open(target)) { text = "Open in DxAudio" };
            button.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                button.style.display = button.GetFirstAncestorOfType<DxAudioAssetDetails>() == null ? DisplayStyle.Flex : DisplayStyle.None;
            });
            root.Insert(0, button);
        }

        internal static void CollectMembers(Object target, string query, List<Member> results)
        {
            results.Clear();
            string filter = query?.Trim() ?? string.Empty;
            if (target is SfxLibrary sfx && sfx != null)
            {
                IReadOnlyList<SfxCue> cues = sfx.Cues;
                for (int index = 0; index < cues.Count; ++index)
                {
                    SfxCue cue = cues[index];
                    AddMember(results, cue, index, cue == null ? string.Empty : cue.Key, filter);
                }
            }

            if (target is MusicLibrary music && music != null)
            {
                IReadOnlyList<MusicTrack> tracks = music.Tracks;
                for (int index = 0; index < tracks.Count; ++index)
                {
                    MusicTrack track = tracks[index];
                    AddMember(results, track, index, track == null ? string.Empty : track.Context, filter);
                }
            }
        }

        private static void AddMember(List<Member> results, Object asset, int index, string lookup, string filter)
        {
            if (filter.Length == 0 || (asset != null && (Contains(asset.name, filter) || Contains(lookup, filter))))
            {
                results.Add(new Member(asset, index + 1, lookup));
            }
        }

        private static bool Contains(string value, string query)
        {
            return value != null && 0 <= value.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddLink(VisualElement row, string label, Object target, Action<Object> open)
        {
            if (target != null)
            {
                row.Add(new Button(() => open(target)) { text = label, tooltip = target.name });
            }
        }

        private static VisualElement BuildLibrary(Object target, Action<Object> open)
        {
            VisualElement view = new();
            TextField search = new("Search Members");
            Label count = new();
            List<Member> members = new();
            ListView list = new()
            {
                itemsSource = members,
                fixedItemHeight = 23f,
                selectionType = SelectionType.None,
                makeItem = () =>
                {
                    Button button = new();
                    button.clicked += () =>
                    {
                        if (button.userData is Object member && member != null)
                        {
                            open(member);
                        }
                    };
                    button.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
                    return button;
                },
                bindItem = (element, index) =>
                {
                    Button button = (Button)element;
                    Member member = members[index];
                    button.userData = member.Asset;
                    button.text = member.Label;
                    button.SetEnabled(member.Asset != null);
                },
            };
            list.style.height = 166f;

            void Filter()
            {
                CollectMembers(target, search.value, members);
                count.text =
                    members.Count == 0 && !string.IsNullOrWhiteSpace(search.value) ? "No members match this search."
                    : members.Count == 1 ? "1 member"
                    : $"{members.Count} members";
                list.style.display = members.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                list.RefreshItems();
            }

            search.RegisterValueChangedCallback(_ => Filter());
            view.Add(search);
            view.Add(count);
            view.Add(list);
            VisualElement findings = DxAudioInspectorUi.AddSection(view, "Library Findings");
            VisualElement report = new();
            void Refresh()
            {
                Filter();
                report.Clear();
                if (target != null)
                {
                    List<string> problems = target is SfxLibrary sfx ? DxAudioValidation.Validate(sfx) : DxAudioValidation.Validate((MusicLibrary)target);
                    DxAudioInspectorUi.AddFindings(report, problems, "Every member is reachable.");
                }
            }

            findings.Add(new Button(Refresh) { text = "Check Again" });
            findings.Add(report);
            Refresh();
            view.TrackSerializedObjectValue(new SerializedObject(target), _ => Refresh());
            return view;
        }
    }
}
