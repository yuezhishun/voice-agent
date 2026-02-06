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
        var wavDir = Environment.GetEnvironmentVariable("REAL_WAV_DIR");
        if (string.IsNullOrWhiteSpace(wavDir))
        {
            return;
        }

        if (!Directory.Exists(wavDir))
        {
            throw new DirectoryNotFoundException($"REAL_WAV_DIR not found: {wavDir}");
        }

        var modelDir = Environment.GetEnvironmentVariable("REAL_PARAFORMER_MODEL_DIR");
        if (string.IsNullOrWhiteSpace(modelDir))
        {
            modelDir = Path.GetFullPath("models/paraformer-online-onnx");
        }

        if (!Directory.Exists(modelDir))
        {
            throw new DirectoryNotFoundException($"Model directory not found: {modelDir}");
        }

        var wavFiles = Directory
            .EnumerateFiles(wavDir, "*.wav", SearchOption.AllDirectories)
            .OrderBy(x => x)
            .ToArray();

        Assert.NotEmpty(wavFiles);

        var options = Options.Create(new AsrMvpOptions
        {
            AsrProvider = "manyspeech",
            ManySpeechParaformer = new ManySpeechParaformerOptions
            {
                ModelDir = modelDir,
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
