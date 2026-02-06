namespace VoiceAgent.AsrMvp.Services;

public sealed class NoopTwoPassRefiner : ITwoPassRefiner
{
    public Task<string> RefineFinalAsync(
        string sessionId,
        string segmentId,
        string onePassFinalText,
        ReadOnlyMemory<float> segmentSamples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(onePassFinalText);
    }

    public void ResetSession(string sessionId)
    {
    }
}
