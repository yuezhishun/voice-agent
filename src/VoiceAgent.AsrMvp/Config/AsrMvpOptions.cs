namespace VoiceAgent.AsrMvp.Config;

public sealed class AsrMvpOptions
{
    public const string SectionName = "AsrMvp";

    public int SampleRate { get; set; } = 16000;
    public int ChunkMs { get; set; } = 320;
    public int TailRollbackSeconds { get; set; } = 2;
    public EndpointingOptions Endpointing { get; set; } = new();
    public AgentOptions Agent { get; set; } = new();
    public TtsOptions Tts { get; set; } = new();
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

public sealed class AgentOptions
{
    public int MaxHistoryTurns { get; set; } = 8;
    public int TimeoutMs { get; set; } = 3000;
    public string SystemPrompt { get; set; } = "You are a concise helpful assistant for voice conversation.";
}

public sealed class TtsOptions
{
    public int SampleRate { get; set; } = 16000;
    public int TimeoutMs { get; set; } = 3000;
    public int ChunkDurationMs { get; set; } = 200;
}
