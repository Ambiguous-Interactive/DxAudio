namespace WallstopStudios.DxAudio.Editor
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Shows what a set of ducks composes to while all of them hold the music.</summary>
    /// <remarks>
    /// Ducks never overwrite one another, so the music hears the most restrictive volume and filters of every
    /// live duck. Add the ducks a scene can raise at once to see that result without entering play mode.
    /// </remarks>
    [CustomEditor(typeof(MusicDuck))]
    public sealed class MusicDuckEditor : UnityEditor.Editor
    {
        private readonly List<MusicDuck> _others = new();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            MusicDuck duck = (MusicDuck)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Composed Preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Add the ducks that can hold the music at the same time to see what they compose to.",
                MessageType.None
            );

            for (int index = 0; index < _others.Count; ++index)
            {
                EditorGUILayout.BeginHorizontal();
                _others[index] = (MusicDuck)EditorGUILayout.ObjectField(_others[index], typeof(MusicDuck), false);
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    _others.RemoveAt(index);
                    --index;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Duck"))
            {
                _others.Add(null);
            }

            MusicAttenuation composed = Compose(duck, _others);
            EditorGUILayout.LabelField("Composed", composed.ToString());
        }

        /// <summary>What <paramref name="duck"/> and <paramref name="others"/> compose to while all are held.</summary>
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
    }
}
