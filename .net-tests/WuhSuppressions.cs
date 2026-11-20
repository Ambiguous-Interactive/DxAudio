[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Style",
    "WUH013",
    Scope = "member",
    Target = "~M:WallstopStudios.DxAudio.DspChain.TryPrepare(System.Int32,System.Int32,System.Int32)",
    Justification = "A custom stage may append another stage while preparing. The indexed loop prepares the new stage in the same call."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Style",
    "WUH013",
    Scope = "member",
    Target = "~M:WallstopStudios.DxAudio.DspChain.Process(System.Single[],System.Int32,System.Int32,System.Int32)",
    Justification = "A custom stage may append another stage while processing. The indexed loop runs the new stage in the same block."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Style",
    "WUH013",
    Scope = "member",
    Target = "~M:WallstopStudios.DxAudio.DspChain.Reset",
    Justification = "A custom stage may change this chain from Reset. Indexed traversal preserves the existing live bound."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Style",
    "WUH013",
    Scope = "member",
    Target = "~P:WallstopStudios.DxAudio.DspChain.GainReductionDecibels",
    Justification = "Custom gain-reduction getters run inside this traversal. Indexed traversal preserves the existing live bound."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.MusicRamp",
    Justification = "Preserve the public struct's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.SfxDropCounts",
    Justification = "Preserve the public struct's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.SfxVoiceState",
    Justification = "Preserve the public struct's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.SfxVariation",
    Justification = "Preserve the serialized public struct's existing equality and hashing contract."
)]

#if DXMESSAGING_PRESENT
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.AudioPauseRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.MusicDuckRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.MusicDuckStartedMessage",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.MusicPlayContextRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.MusicStopRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.MusicStoppedMessage",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.MusicTrackChangedMessage",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.MusicUnduckRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.SfxCueDroppedMessage",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.SfxLoopRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.SfxLoopStartedMessage",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.SfxLoopStopRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.SfxMuteRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.SfxPlayRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "WUH023",
    Scope = "type",
    Target = "~T:WallstopStudios.DxAudio.Integration.SfxStopAllRequest",
    Justification = "Preserve the public DxMessaging message's existing equality and hashing contract."
)]
#endif
