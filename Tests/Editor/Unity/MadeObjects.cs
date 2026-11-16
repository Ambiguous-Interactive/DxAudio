namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Destroys every Unity object a single test made when the test's <c>using</c> scope ends.
    /// </summary>
    /// <remarks>
    /// One instance per test body keeps ownership visible and stops a failing test from leaking its objects into the
    /// next one. Loop handles are not tracked: a test stops a loop through <c>SfxPlayer.StopLoop</c>.
    /// </remarks>
    internal sealed class MadeObjects : IDisposable
    {
        private readonly List<Object> _made = new();

        public T Track<T>(T made)
            where T : Object
        {
            _made.Add(made);
            return made;
        }

        public void Dispose()
        {
            for (int index = 0; index < _made.Count; ++index)
            {
                Object made = _made[index];
                if (made != null)
                {
                    Object.DestroyImmediate(made);
                }
            }

            _made.Clear();
        }
    }
}
