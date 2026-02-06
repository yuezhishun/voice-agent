namespace VoiceAgent.AsrMvp.Services;

public interface ITtsEngine
{
    IAsyncEnumerable<byte[]> SynthesizePcm16Async(
        string sessionId,
        string text,
        int sampleRate,
        int chunkDurationMs,
        CancellationToken cancellationToken);
}
