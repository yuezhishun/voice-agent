namespace VoiceAgent.AsrMvp.Services;

public interface IStreamingAsrEngine
{
    Task<string> DecodePartialAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken);
    Task<string> DecodeFinalAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken);
}
