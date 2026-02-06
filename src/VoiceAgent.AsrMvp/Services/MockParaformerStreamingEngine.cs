namespace VoiceAgent.AsrMvp.Services;

public sealed class MockParaformerStreamingEngine : IStreamingAsrEngine
{
    public Task<string> DecodePartialAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        var chars = Math.Max(1, samples.Length / 6400);
        var text = $"[partial:{sessionId}]" + new string('字', chars);
        return Task.FromResult(text);
    }

    public Task<string> DecodeFinalAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        var chars = Math.Max(3, samples.Length / 4800);
        var text = $"[final:{sessionId}]" + new string('字', chars) + "。";
        return Task.FromResult(text);
    }
}
