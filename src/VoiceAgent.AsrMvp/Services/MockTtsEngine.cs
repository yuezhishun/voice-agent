using System.Runtime.CompilerServices;

namespace VoiceAgent.AsrMvp.Services;

public sealed class MockTtsEngine : ITtsEngine
{
    public async IAsyncEnumerable<byte[]> SynthesizePcm16Async(
        string sessionId,
        string text,
        int sampleRate,
        int chunkDurationMs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var totalChunks = Math.Clamp(Math.Max(1, text.Length / 8), 1, 8);
        var sampleCount = Math.Max(1, sampleRate * chunkDurationMs / 1000);

        for (var c = 0; c < totalChunks; c++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytes = new byte[sampleCount * 2];
            for (var i = 0; i < sampleCount; i++)
            {
                var t = (c * sampleCount + i) / (double)sampleRate;
                var v = 0.2 * Math.Sin(2 * Math.PI * 220 * t);
                var s = (short)Math.Clamp((int)(v * 32767), short.MinValue, short.MaxValue);
                bytes[i * 2] = (byte)(s & 0xff);
                bytes[(i * 2) + 1] = (byte)((s >> 8) & 0xff);
            }

            yield return bytes;
            await Task.Delay(15, cancellationToken);
        }
    }
}
