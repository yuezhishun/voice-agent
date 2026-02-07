using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Domain;
using VoiceAgent.AsrMvp.Pipeline;

namespace VoiceAgent.AsrMvp.Tests.Unit;

public sealed class EndpointingEngineTests
{
    private static readonly float[] SpeechChunk = Enumerable.Repeat(0.12f, 5120).ToArray();
    private static readonly float[] SilenceChunk = new float[5120];

    [Fact]
    public void FinalizesAfterEnoughSpeechThenSilence()
    {
        var options = Options.Create(new AsrMvpOptions());
        var engine = new EndpointingEngine(options);
        var session = new SessionContext("s-1");

        var now = 1000L;

        for (var i = 0; i < 4; i++)
        {
            var decision = engine.Process(session, SpeechChunk, speech: true, chunkMs: 320, nowMs: now, frameRms: 0.03f);
            Assert.False(decision.ShouldFinalize);
            now += 320;
        }

        EndpointingDecision last = new();
        for (var i = 0; i < 3; i++)
        {
            last = engine.Process(session, SilenceChunk, speech: false, chunkMs: 320, nowMs: now, frameRms: 0.0f);
            now += 320;
        }

        Assert.True(last.ShouldFinalize);
        Assert.Equal("endpointing", last.FinalReason);
        Assert.NotNull(last.FinalSegmentSamples);
        Assert.NotEmpty(last.FinalSegmentSamples!);
    }

    [Fact]
    public void FinalizesWhenMaxSegmentReached()
    {
        var options = Options.Create(new AsrMvpOptions());
        var engine = new EndpointingEngine(options);
        var session = new SessionContext("s-2");

        var now = 1000L;
        EndpointingDecision last = new();

        for (var i = 0; i < 50; i++)
        {
            last = engine.Process(session, SpeechChunk, speech: true, chunkMs: 320, nowMs: now, frameRms: 0.03f);
            now += 320;
            if (last.ShouldFinalize)
            {
                break;
            }
        }

        Assert.True(last.ShouldFinalize);
        Assert.Equal("max_segment", last.FinalReason);
        Assert.NotNull(last.FinalSegmentSamples);
        Assert.NotEmpty(last.FinalSegmentSamples!);
    }

    [Fact]
    public void SwitchesToNoisyProfileWhenNoisePersists()
    {
        var options = Options.Create(new AsrMvpOptions());
        var engine = new EndpointingEngine(options);
        var session = new SessionContext("s-3");

        var now = 0L;
        for (var i = 0; i < 20; i++)
        {
            var decision = engine.Process(session, SilenceChunk, speech: false, chunkMs: 320, nowMs: now, frameRms: 0.04f);
            now += 320;
            if (i == 19)
            {
                Assert.Equal("noisy", decision.EndpointingProfile);
            }
        }
    }
}
