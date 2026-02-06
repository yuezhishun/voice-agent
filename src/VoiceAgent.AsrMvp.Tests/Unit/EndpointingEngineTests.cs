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
            var decision = engine.Process(session, SpeechChunk, speech: true, chunkMs: 320, nowMs: now);
            Assert.False(decision.ShouldFinalize);
            now += 320;
        }

        EndpointingDecision last = new();
        for (var i = 0; i < 3; i++)
        {
            last = engine.Process(session, SilenceChunk, speech: false, chunkMs: 320, nowMs: now);
            now += 320;
        }

        Assert.True(last.ShouldFinalize);
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
            last = engine.Process(session, SpeechChunk, speech: true, chunkMs: 320, nowMs: now);
            now += 320;
            if (last.ShouldFinalize)
            {
                break;
            }
        }

        Assert.True(last.ShouldFinalize);
        Assert.NotNull(last.FinalSegmentSamples);
        Assert.NotEmpty(last.FinalSegmentSamples!);
    }
}
