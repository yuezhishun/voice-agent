using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Pipeline;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class FileAsrPipelineTests
{
    [Fact]
    public async Task SpeechFileProducesAtLeastOneFinalResult()
    {
        var sut = CreateProcessor();
        var path = FindAsset("speech_then_silence.wav");

        var finals = await sut.ProcessWavFileAsync(path);

        Assert.NotEmpty(finals);
        Assert.All(finals, s => Assert.False(string.IsNullOrWhiteSpace(s)));
    }

    [Fact]
    public async Task SilenceFileProducesNoFinalResult()
    {
        var sut = CreateProcessor();
        var path = FindAsset("silence.wav");

        var finals = await sut.ProcessWavFileAsync(path);

        Assert.Empty(finals);
    }

    private static AsrFileProcessor CreateProcessor()
    {
        var options = Options.Create(new AsrMvpOptions());
        return new AsrFileProcessor(
            options,
            new BasicAudioPreprocessor(),
            new BasicAudioClassifier(),
            new BasicAudioQualityChecker(),
            new EnergyVad(options),
            new EndpointingEngine(options),
            new MockParaformerStreamingEngine());
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
