namespace VoiceAgent.AsrMvp.Domain;

public sealed class EndpointingState
{
    public bool InSpeech { get; set; }
    public bool PendingFinalize { get; set; }
    public int SegmentDurationMs { get; set; }
    public int SpeechMs { get; set; }
    public int SilenceMs { get; set; }
    public int PendingFinalizeMs { get; set; }
    public long SegmentStartMs { get; set; }

    public void Reset()
    {
        InSpeech = false;
        PendingFinalize = false;
        SegmentDurationMs = 0;
        SpeechMs = 0;
        SilenceMs = 0;
        PendingFinalizeMs = 0;
        SegmentStartMs = 0;
    }
}
