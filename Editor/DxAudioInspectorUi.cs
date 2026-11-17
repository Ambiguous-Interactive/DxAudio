namespace WallstopStudios.DxAudio.Editor
{
    using System.Collections.Generic;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>The headings, rows and help lines the four DxAudio authoring surfaces share.</summary>
    /// <remarks>
    /// Every surface draws with UI Toolkit, so one helper keeps the spacing and the warning shape the same. A
    /// finding is a <see cref="HelpBox" />, and a clear surface says so instead of showing nothing.
    /// </remarks>
    internal static class DxAudioInspectorUi
    {
        /// <summary>Adds a spaced section under <paramref name="parent" /> and returns it.</summary>
        public static VisualElement AddSection(VisualElement parent, string title)
        {
            VisualElement section = new VisualElement();
            section.style.marginTop = 8f;
            Label heading = new Label(title);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(heading);
            parent.Add(section);
            return section;
        }

        /// <summary>A row that holds a description and the control that acts on it.</summary>
        public static VisualElement Row()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        /// <summary>Adds a label that takes the row's spare width and returns it.</summary>
        public static Label AddText(VisualElement parent, string text)
        {
            Label label = new Label(text);
            label.style.flexGrow = 1f;
            parent.Add(label);
            return label;
        }

        /// <summary>Adds one warning per finding, or one clear line when there are none.</summary>
        public static void AddFindings(VisualElement parent, IReadOnlyList<string> findings, string clear)
        {
            if (findings.Count == 0)
            {
                parent.Add(new HelpBox(clear, HelpBoxMessageType.Info));
                return;
            }

            for (int index = 0; index < findings.Count; ++index)
            {
                parent.Add(new HelpBox(findings[index], HelpBoxMessageType.Warning));
            }
        }
    }
}
