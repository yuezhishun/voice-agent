namespace VoiceAgent.AsrMvp.Services;

public interface ITwoPassRefiner
{
    Task<string> RefineFinalAsync(
        string sessionId,
        string segmentId,
        string onePassFinalText,
        ReadOnlyMemory<float> segmentSamples,
        int sampleRate,
        CancellationToken cancellationToken);

    void ResetSession(string sessionId);
}
