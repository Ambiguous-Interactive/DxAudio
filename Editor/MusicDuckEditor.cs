namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Shows what a set of ducks composes to while all of them hold the music.</summary>
    /// <remarks>
    /// Ducks never overwrite one another, so the music hears the most restrictive volume and filters of every
    /// live duck. Add the ducks a scene can raise at once to see that result without entering play mode.
    /// </remarks>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(MusicDuck))]
    public sealed class MusicDuckEditor : UnityEditor.Editor
    {
        private const string ComposedName = "DxAudioComposed";

        // One set across inspectors: the question is what the ducks a scene raises compose to.
        private static readonly List<MusicDuck> _preview = new();

        // Every open preview draws the shared set, so a change in one inspector redraws all of them.
        private static readonly List<Action> _refreshers = new();

        /// <summary>The composed preview for <paramref name="preview" />, which the added and removed rows edit.</summary>
        public static VisualElement BuildComposedPreview(MusicDuck duck, List<MusicDuck> preview)
        {
            return BuildComposedPreview(duck, preview, false, out _);
        }

        /// <summary>What <paramref name="duck" /> and <paramref name="others" /> compose to while all are held.</summary>
        public static MusicAttenuation Compose(MusicDuck duck, IReadOnlyList<MusicDuck> others)
        {
            MusicDuckStack stack = new();
            if (duck != null)
            {
                stack.Take(duck, duck.Attenuation);
            }

            if (others != null)
            {
                for (int index = 0; index < others.Count; ++index)
                {
                    MusicDuck other = others[index];
                    if (other == null || other == duck || stack.IsActive(other))
                    {
                        continue;
                    }

                    stack.Take(other, other.Attenuation, other);
                }
            }

            return stack.Current;
        }

        /// <summary>The composed preview for the shared set and the action that redraws it.</summary>
        private static VisualElement BuildComposedPreview(MusicDuck duck, out Action refresh)
        {
            return BuildComposedPreview(duck, _preview, true, out refresh);
        }

        private static VisualElement BuildComposedPreview(MusicDuck duck, List<MusicDuck> preview, bool shared, out Action refresh)
        {
            VisualElement view = new VisualElement();
            VisualElement rows = new VisualElement();
            Label composed = new Label { name = ComposedName };
            view.Add(rows);

            void Refresh()
            {
                FillRows(duck, preview, rows, composed, Notify);
            }

            void Notify()
            {
                if (!shared)
                {
                    Refresh();
                    return;
                }

                // A change in one inspector redraws every open preview, so no row index goes stale.
                for (int index = _refreshers.Count - 1; 0 <= index; --index)
                {
                    _refreshers[index]();
                }
            }

            Action refreshView = Refresh;
            Button add = new Button(() =>
            {
                preview.Add(null);
                Notify();
            })
            {
                text = "Add Duck",
            };

            view.Add(add);
            view.Add(composed);
            if (shared)
            {
                view.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    if (!_refreshers.Contains(refreshView))
                    {
                        _refreshers.Add(refreshView);
                    }
                });
                view.RegisterCallback<DetachFromPanelEvent>(_ => _refreshers.Remove(refreshView));
            }

            Refresh();
            refresh = refreshView;
            return view;
        }

        private static void FillRows(MusicDuck duck, List<MusicDuck> preview, VisualElement rows, Label composed, Action afterChange)
        {
            rows.Clear();
            for (int index = 0; index < preview.Count; ++index)
            {
                int rowIndex = index;
                VisualElement row = DxAudioInspectorUi.Row();
                ObjectField field = new ObjectField
                {
                    objectType = typeof(MusicDuck),
                    allowSceneObjects = false,
                    value = preview[rowIndex],
                };
                field.style.flexGrow = 1f;
                field.RegisterValueChangedCallback(change =>
                {
                    if (0 <= rowIndex && rowIndex < preview.Count)
                    {
                        preview[rowIndex] = (MusicDuck)change.newValue;
                    }

                    afterChange();
                });
                row.Add(field);

                Button remove = new Button(() =>
                {
                    if (0 <= rowIndex && rowIndex < preview.Count)
                    {
                        preview.RemoveAt(rowIndex);
                    }

                    afterChange();
                })
                {
                    text = "Remove",
                };
                remove.style.width = 70f;
                row.Add(remove);
                rows.Add(row);
            }

            ShowComposed(duck, preview, composed);
        }

        private static void ShowComposed(MusicDuck duck, List<MusicDuck> preview, Label composed)
        {
            composed.text = "Composed: " + Compose(duck, preview);
        }

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            // The preview composes one duck with a chosen set, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return root;
            }

            DxAudioAssetDetails.AddWorkspaceButton(root, target);
            VisualElement section = DxAudioInspectorUi.AddSection(root, "Composed Preview");
            section.Add(new HelpBox("Add the ducks that can hold the music at the same time to see what they compose to.", HelpBoxMessageType.None));
            VisualElement preview = BuildComposedPreview((MusicDuck)target, out Action refresh);
            section.Add(preview);
            // The composed line depends on this duck's own fields, so a serialized change redraws it.
            preview.TrackSerializedObjectValue(serializedObject, _ => refresh());
            return root;
        }
    }
}
