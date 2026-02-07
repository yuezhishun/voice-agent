using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Unit;

public sealed class AsrPipelineMetricsTests
{
    [Fact]
    public void Snapshot_ContainsStageStatsAndInterruptCount()
    {
        var metrics = new AsrPipelineMetrics(Options.Create(new AsrMvpOptions()));

        metrics.OnSessionOpened("s1");
        metrics.OnChunkProcessed("s1", 320);
        metrics.OnPartial();
        metrics.OnFinal("s1");
        metrics.OnStageSuccess("asr", 80);
        metrics.OnStageFailure("agent");
        metrics.OnInterrupt();
        metrics.OnSessionClosed("s1");

        var snapshot = metrics.Snapshot();

        Assert.Equal(1, snapshot.SessionsOpened);
        Assert.Equal(1, snapshot.SessionsClosed);
        Assert.Equal(1, snapshot.InterruptCount);
        Assert.Contains(snapshot.Stages, x => x.Stage == "asr" && x.SuccessCount == 1);
        Assert.Contains(snapshot.Stages, x => x.Stage == "agent" && x.FailureCount == 1);
    }
}
