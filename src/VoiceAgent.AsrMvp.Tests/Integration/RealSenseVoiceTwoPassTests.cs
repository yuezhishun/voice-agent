using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Services;
using VoiceAgent.AsrMvp.Utils;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class RealSenseVoiceTwoPassTests
{
    [Fact]
    public async Task SenseVoiceTwoPassRefiner_CanRefine_WhenModelProvided()
    {
        var settings = RealIntegrationTestSettings.Load();
        var modelDir = settings.SenseVoiceModelDir;

        if (!settings.Enabled || string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(modelDir))
        {
            return;
        }

        var wav = ResolveRepoPath("src/VoiceAgent.AsrMvp.Tests/TestAssets/speech_then_silence.wav");
        var (samples, sampleRate, channels) = WavFileReader.ReadPcm16(wav);
        if (channels > 1)
        {
            samples = ToMono(samples, channels);
        }

        var options = Options.Create(new AsrMvpOptions
        {
            TwoPass = new TwoPassOptions
            {
                Enabled = true,
                Provider = "sensevoice",
                WindowSeconds = 12,
                WindowSegments = 3,
                PrefixLockEnabled = true,
                SenseVoice = new SenseVoiceOfflineOptions
                {
                    ModelDir = modelDir,
                    ModelFile = "model.int8.onnx",
                    Language = "zh",
                    UseInverseTextNormalization = true,
                    NumThreads = 2,
                    Provider = "cpu"
                }
            }
        });

        using var refiner = new SenseVoiceTwoPassRefiner(options, NullLogger<SenseVoiceTwoPassRefiner>.Instance);
        var text = await refiner.RefineFinalAsync(
            sessionId: "s1",
            segmentId: "seg-1",
            onePassFinalText: "测试一段文本。",
            segmentSamples: samples,
            sampleRate: sampleRate,
            cancellationToken: CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    private static string ResolveRepoPath(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return Path.GetFullPath(relativePath);
    }

    private static float[] ToMono(float[] interleaved, int channels)
    {
        var frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += interleaved[(i * channels) + c];
            }

            mono[i] = sum / channels;
        }

        return mono;
    }
}
