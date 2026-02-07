using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Pipeline;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class RealWavBatchManySpeechTests
{
    [Fact]
    public async Task ManySpeechProvider_CanProcessRealWavDirectory_WhenConfigured()
    {
        var settings = RealIntegrationTestSettings.Load();
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.RealWavDir))
        {
            return;
        }

        if (!Directory.Exists(settings.RealWavDir))
        {
            throw new DirectoryNotFoundException($"RealIntegration:RealWavDir not found: {settings.RealWavDir}");
        }

        if (string.IsNullOrWhiteSpace(settings.ParaformerModelDir))
        {
            return;
        }

        if (!Directory.Exists(settings.ParaformerModelDir))
        {
            throw new DirectoryNotFoundException($"RealIntegration:ParaformerModelDir not found: {settings.ParaformerModelDir}");
        }

        var wavFiles = Directory
            .EnumerateFiles(settings.RealWavDir, "*.wav", SearchOption.AllDirectories)
            .OrderBy(x => x)
            .ToArray();

        Assert.NotEmpty(wavFiles);

        var options = Options.Create(new AsrMvpOptions
        {
            AsrProvider = "manyspeech",
            ManySpeechParaformer = new ManySpeechParaformerOptions
            {
                ModelDir = settings.ParaformerModelDir,
                Threads = 2,
                AutoGenerateTokensTxt = true
            }
        });

        using var asr = new ManySpeechParaformerOnlineEngine(options);
        var processor = new AsrFileProcessor(
            options,
            new BasicAudioPreprocessor(),
            new BasicAudioClassifier(),
            new BasicAudioQualityChecker(),
            new EnergyVad(options),
            new EndpointingEngine(options),
            asr);

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
        var outFile = Path.Combine(outDir, $"real_wav_asr_manyspeech_report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
        await File.WriteAllLinesAsync(outFile, report);

        Assert.True(filesWithText > 0, $"No wav file produced text. Report: {outFile}");
    }
}
