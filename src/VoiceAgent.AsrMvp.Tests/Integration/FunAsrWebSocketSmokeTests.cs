using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Pipeline;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class FunAsrWebSocketSmokeTests
{
    [Fact]
    public async Task FunAsrWebSocketProvider_CanProcessSpeechFile_WhenServerConfigured()
    {
        var url = Environment.GetEnvironmentVariable("FUNASR_WS_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var options = Options.Create(new AsrMvpOptions
        {
            AsrProvider = "funasr",
            FunAsrWebSocket = new FunAsrWebSocketOptions
            {
                Url = url,
                Mode = "online",
                FinalTimeoutMs = 8000,
                ReceiveTimeoutMs = 500
            }
        });

        var processor = new AsrFileProcessor(
            options,
            new BasicAudioPreprocessor(),
            new BasicAudioClassifier(),
            new BasicAudioQualityChecker(),
            new EnergyVad(options),
            new EndpointingEngine(options),
            new FunAsrWebSocketStreamingEngine(options));

        var wav = FindAsset("speech_then_silence.wav");
        var results = await processor.ProcessWavFileAsync(wav);

        Assert.NotNull(results);
    }

    private static string FindAsset(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "TestAssets", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Test asset not found: {fileName}");
    }
}
