using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class RealKokoroTtsTests
{
    [Fact]
    public async Task Kokoro_CanSynthesize_WhenModelDirectoryProvided()
    {
        var modelDir = Environment.GetEnvironmentVariable("KOKORO_MODEL_DIR");
        if (string.IsNullOrWhiteSpace(modelDir))
        {
            return;
        }

        if (!Directory.Exists(modelDir))
        {
            throw new DirectoryNotFoundException($"KOKORO_MODEL_DIR not found: {modelDir}");
        }

        var options = Options.Create(new AsrMvpOptions
        {
            Tts = new TtsOptions
            {
                Provider = "kokoro",
                SampleRate = 24000,
                ChunkDurationMs = 200,
                Kokoro = new KokoroTtsOptions
                {
                    ModelDir = modelDir
                }
            }
        });

        using var tts = new KokoroTtsEngine(options, NullLogger<KokoroTtsEngine>.Instance);

        var chunks = 0;
        var bytes = 0;
        await foreach (var chunk in tts.SynthesizePcm16Async(
                           "s1",
                           "你好，欢迎使用语音交互系统。",
                           options.Value.Tts.SampleRate,
                           options.Value.Tts.ChunkDurationMs,
                           CancellationToken.None))
        {
            chunks++;
            bytes += chunk.Length;
        }

        Assert.True(chunks > 0, "Kokoro synthesis returned no chunks.");
        Assert.True(bytes > 0, "Kokoro synthesis returned empty audio.");
    }
}
