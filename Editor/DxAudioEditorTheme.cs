namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEngine.UIElements;

    internal static class DxAudioEditorTheme
    {
        // The asset can move with the package. Its GUID works for registry and embedded installs.
        private const string StyleSheetGuid = "58d7023c818049ba886fb354820380fd";

        internal static void Apply(VisualElement element)
        {
            StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(StyleSheetGuid));
            if (sheet != null && !element.styleSheets.Contains(sheet))
            {
                element.styleSheets.Add(sheet);
            }

            element.AddToClassList("dxaudio");
            element.EnableInClassList("dxaudio--light", !EditorGUIUtility.isProSkin);
        }
    }
}
