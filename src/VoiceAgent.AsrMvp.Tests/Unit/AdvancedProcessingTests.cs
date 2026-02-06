using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Unit;

public sealed class AdvancedProcessingTests
{
    [Fact]
    public void AdaptiveVad_UsesAdaptiveThreshold()
    {
        var options = Options.Create(new AsrMvpOptions
        {
            Endpointing = new EndpointingOptions
            {
                EnergyThreshold = 0.02f,
                AdaptiveVadEnabled = true,
                AdaptiveVadFloor = 0.003f,
                AdaptiveVadMultiplier = 2.0f,
                AdaptiveVadWindowFrames = 20
            }
        });

        var vad = new EnergyVad(options);
        var noise = Enumerable.Repeat(0.002f, 320).ToArray();
        var speech = Enumerable.Repeat(0.08f, 320).ToArray();

        for (var i = 0; i < 30; i++)
        {
            _ = vad.IsSpeech(noise);
        }

        Assert.True(vad.IsSpeech(speech));
    }

    [Fact]
    public void TranscriptPostProcessor_AppendsPunctuationForFinal()
    {
        var options = Options.Create(new AsrMvpOptions
        {
            PostProcess = new PostProcessOptions
            {
                EnableNormalization = true,
                EnablePunctuation = true
            }
        });

        var pp = new TranscriptPostProcessor(options);
        var final = pp.Process(" 你好 世界 ");

        Assert.Equal("你好 世界。", final);
    }
}
