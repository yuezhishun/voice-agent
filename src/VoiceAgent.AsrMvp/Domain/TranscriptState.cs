namespace VoiceAgent.AsrMvp.Domain;

public sealed class TranscriptState
{
    public string LastPartial { get; set; } = string.Empty;
    public long LastPartialSentAtMs { get; set; }
}
