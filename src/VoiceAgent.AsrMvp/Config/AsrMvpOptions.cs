namespace VoiceAgent.AsrMvp.Config;

public sealed class AsrMvpOptions
{
    public const string SectionName = "AsrMvp";

    public int SampleRate { get; set; } = 16000;
    public int ChunkMs { get; set; } = 320;
    public int TailRollbackSeconds { get; set; } = 2;
    public EndpointingOptions Endpointing { get; set; } = new();
}

public sealed class EndpointingOptions
{
    public int EndSilenceMs { get; set; } = 800;
    public int MinSegmentMs { get; set; } = 1200;
    public int MinSilenceMs { get; set; } = 300;
    public int MergeBackMs { get; set; } = 300;
    public int MaxSegmentMs { get; set; } = 15000;
    public float EnergyThreshold { get; set; } = 0.012f;
}
