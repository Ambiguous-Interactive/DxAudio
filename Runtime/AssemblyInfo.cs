using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WallstopStudios.DxAudio.Tests.Editor")]
[assembly: InternalsVisibleTo("WallstopStudios.DxAudio.Tests.Integration.DxMessaging")]

#if UNITY_EDITOR
[assembly: InternalsVisibleTo("WallstopStudios.DxAudio.Editor")]
#endif
