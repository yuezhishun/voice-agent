using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Pipeline;

namespace VoiceAgent.AsrMvp.Tests.Unit;

public sealed class TranscriptStabilizerTests
{
    [Fact]
    public void KeepsFrozenPrefixWhenCurrentRewritesTooMuch()
    {
        var stabilizer = new TranscriptStabilizer(Options.Create(new AsrMvpOptions
        {
            TailRollbackSeconds = 2
        }));

        var previous = "你好今天北京天气";
        var current = "你们今天天气";

        var stabilized = stabilizer.Stabilize(previous, current);

        Assert.StartsWith("你", stabilized);
    }

    [Fact]
    public void ReturnsCurrentWhenCompatibleWithFrozenPrefix()
    {
        var stabilizer = new TranscriptStabilizer(Options.Create(new AsrMvpOptions
        {
            TailRollbackSeconds = 2
        }));

        var previous = "abcdefghi";
        var current = "abcdeXYZ";

        var stabilized = stabilizer.Stabilize(previous, current);

        Assert.Equal(current, stabilized);
    }
}
