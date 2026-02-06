namespace VoiceAgent.AsrMvp.Domain;

public sealed class EndpointingDecision
{
    public bool ShouldAppendAudio { get; init; }
    public bool ShouldFinalize { get; init; }
    public bool InSpeech { get; init; }
    public int SegmentDurationMs { get; init; }
    public long SegmentStartMs { get; init; }
    public long SegmentEndMs { get; init; }
    public float[]? FinalSegmentSamples { get; init; }
}
