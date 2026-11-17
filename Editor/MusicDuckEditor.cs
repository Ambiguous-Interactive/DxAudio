namespace WallstopStudios.DxAudio.Editor
{
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

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            // The preview composes one duck with a chosen set, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return root;
            }

            VisualElement section = DxAudioInspectorUi.AddSection(root, "Composed Preview");
            section.Add(new HelpBox("Add the ducks that can hold the music at the same time to see what they compose to.", HelpBoxMessageType.None));
            section.Add(BuildComposedPreview((MusicDuck)target));
            return root;
        }

        /// <summary>The composed preview for the set the inspectors share.</summary>
        public static VisualElement BuildComposedPreview(MusicDuck duck)
        {
            return BuildComposedPreview(duck, _preview);
        }

        /// <summary>The composed preview for <paramref name="preview" />, which the added and removed rows edit.</summary>
        public static VisualElement BuildComposedPreview(MusicDuck duck, List<MusicDuck> preview)
        {
            VisualElement view = new VisualElement();
            VisualElement rows = new VisualElement();
            Label composed = new Label { name = ComposedName };
            view.Add(rows);

            Button add = new Button(() =>
            {
                preview.Add(null);
                FillRows(duck, preview, rows, composed);
            })
            {
                text = "Add Duck",
            };

            view.Add(add);
            view.Add(composed);
            FillRows(duck, preview, rows, composed);
            return view;
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

        private static void FillRows(MusicDuck duck, List<MusicDuck> preview, VisualElement rows, Label composed)
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
                    preview[rowIndex] = (MusicDuck)change.newValue;
                    ShowComposed(duck, preview, composed);
                });
                row.Add(field);

                Button remove = new Button(() =>
                {
                    preview.RemoveAt(rowIndex);
                    FillRows(duck, preview, rows, composed);
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
    }
}
