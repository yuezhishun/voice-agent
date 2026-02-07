using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Pipeline;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class RealWavBatchTests
{
    [Fact]
    public async Task FunAsrWebSocketProvider_CanProcessRealWavDirectory_WhenConfigured()
    {
        var settings = RealIntegrationTestSettings.Load();
        if (!settings.Enabled ||
            string.IsNullOrWhiteSpace(settings.FunAsrWebSocketUrl) ||
            string.IsNullOrWhiteSpace(settings.RealWavDir))
        {
            return;
        }

        if (!Directory.Exists(settings.RealWavDir))
        {
            throw new DirectoryNotFoundException($"RealIntegration:RealWavDir not found: {settings.RealWavDir}");
        }

        var wavFiles = Directory
            .EnumerateFiles(settings.RealWavDir, "*.wav", SearchOption.AllDirectories)
            .OrderBy(x => x)
            .ToArray();

        Assert.NotEmpty(wavFiles);

        var options = Options.Create(new AsrMvpOptions
        {
            AsrProvider = "funasr",
            FunAsrWebSocket = new FunAsrWebSocketOptions
            {
                Url = settings.FunAsrWebSocketUrl,
                Mode = "online",
                FinalTimeoutMs = 12000,
                ReceiveTimeoutMs = 800
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

        var report = new List<string>();
        var filesWithText = 0;

        foreach (var wav in wavFiles)
        {
            var finals = await processor.ProcessWavFileAsync(wav);
            var chars = finals.Sum(x => x.Length);
            if (chars > 0)
            {
                filesWithText++;
            }

            report.Add($"{wav}\tsegments={finals.Count}\tchars={chars}");
        }

        var outDir = Path.Combine(Path.GetTempPath(), "voice-agent-reports");
        Directory.CreateDirectory(outDir);
        var outFile = Path.Combine(outDir, $"real_wav_asr_report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
        await File.WriteAllLinesAsync(outFile, report);

        Assert.True(filesWithText > 0, $"No wav file produced text. Report: {outFile}");
    }
}
