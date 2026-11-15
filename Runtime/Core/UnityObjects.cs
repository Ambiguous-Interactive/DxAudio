namespace WallstopStudios.DxAudio
{
    /// <summary>Sees a destroyed Unity object, which generic and <c>object</c>-typed code cannot.</summary>
    /// <remarks>
    /// Unity overloads <c>==</c> on <c>UnityEngine.Object</c>, so a destroyed asset or component compares equal
    /// to null. That overload is static: code that holds the object as <c>object</c> or as a type parameter
    /// calls the reference check, and <c>??</c> never calls an overload. Such code sees a destroyed object as
    /// alive unless it asks here. Off the engine there is no destroyed object, so the core still compiles and
    /// its tests run on plain .NET.
    /// </remarks>
    internal static class UnityObjects
    {
        /// <summary>Whether <paramref name="value"/> is a Unity object that has been destroyed.</summary>
        internal static bool IsDestroyed(object value)
        {
#if UNITY_5_3_OR_NEWER
            return value is UnityEngine.Object unityObject && unityObject == null;
#else
            return false;
#endif
        }

        /// <summary>Whether <paramref name="value"/> is null or a destroyed Unity object.</summary>
        internal static bool IsGone(object value)
        {
            return value == null || IsDestroyed(value);
        }
    }
}
