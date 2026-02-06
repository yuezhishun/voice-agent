using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Unit;

public sealed class AudioProcessingTests
{
    [Fact]
    public void PreprocessorComputesExpectedMetrics()
    {
        var pre = new BasicAudioPreprocessor();
        var input = Enumerable.Repeat(0.2f, 3200).ToArray();

        var result = pre.Process(input);

        Assert.Equal(input.Length, result.Samples.Length);
        Assert.True(result.Peak <= 1f);
        Assert.True(result.Rms >= 0f);
    }

    [Fact]
    public void ClassifierMarksSilenceAsNonSpeech()
    {
        var cls = new BasicAudioClassifier();
        var frame = new AudioPreprocessResult(new float[3200], 0.001f, 0.001f, false);

        var kind = cls.Classify(frame);

        Assert.Equal(AudioKind.NonSpeech, kind);
    }

    [Fact]
    public void QualityCheckerRejectsClippedFrames()
    {
        var checker = new BasicAudioQualityChecker();
        var clipped = new AudioPreprocessResult(Enumerable.Repeat(1f, 3200).ToArray(), 0.8f, 1f, true);

        Assert.False(checker.IsAcceptable(clipped));
    }

    [Fact]
    public void QualityCheckerAcceptsNormalSpeechFrame()
    {
        var checker = new BasicAudioQualityChecker();
        var normal = new AudioPreprocessResult(Enumerable.Repeat(0.2f, 3200).ToArray(), 0.2f, 0.2f, false);

        Assert.True(checker.IsAcceptable(normal));
    }
}
