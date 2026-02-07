using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class AsrPipelineMetrics
{
    private sealed class SessionTrack
    {
        public Stopwatch AsrWatch { get; } = Stopwatch.StartNew();
        public long AudioMs { get; set; }
        public int Segments { get; set; }
    }

    private sealed class StageTrack
    {
        public long SuccessCount;
        public long FailureCount;
        public readonly Queue<int> LatenciesMs = new();
    }

    private readonly MetricsOptions _options;
    private readonly ConcurrentDictionary<string, SessionTrack> _sessions = new();
    private readonly ConcurrentDictionary<string, StageTrack> _stages = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stageLock = new();

    private long _sessionsOpened;
    private long _sessionsClosed;
    private long _partialCount;
    private long _finalCount;
    private long _interruptCount;
    private long _errorCount;
    private long _totalAsrMs;
    private long _totalAudioMs;

    public AsrPipelineMetrics(IOptions<AsrMvpOptions> options)
    {
        _options = options.Value.Metrics;
    }

    public void OnSessionOpened(string sessionId)
    {
        if (!_options.Enabled)
        {
            return;
        }

        Interlocked.Increment(ref _sessionsOpened);
        _sessions[sessionId] = new SessionTrack();
    }

    public void OnSessionClosed(string sessionId)
    {
        if (!_options.Enabled)
        {
            return;
        }

        Interlocked.Increment(ref _sessionsClosed);
        _sessions.TryRemove(sessionId, out _);
    }

    public void OnChunkProcessed(string sessionId, int chunkMs)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (_sessions.TryGetValue(sessionId, out var track))
        {
            track.AudioMs += chunkMs;
        }
    }

    public void OnPartial() => Interlocked.Increment(ref _partialCount);

    public void OnFinal(string sessionId)
    {
        if (!_options.Enabled)
        {
            return;
        }

        Interlocked.Increment(ref _finalCount);
        if (_sessions.TryGetValue(sessionId, out var track))
        {
            track.Segments++;
            Interlocked.Add(ref _totalAsrMs, track.AsrWatch.ElapsedMilliseconds);
            Interlocked.Add(ref _totalAudioMs, track.AudioMs);
            track.AsrWatch.Restart();
            track.AudioMs = 0;
        }
    }

    public void OnInterrupt()
    {
        Interlocked.Increment(ref _interruptCount);
    }

    public void OnStageSuccess(string stage, int latencyMs)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var track = _stages.GetOrAdd(stage, _ => new StageTrack());
        Interlocked.Increment(ref track.SuccessCount);

        lock (_stageLock)
        {
            track.LatenciesMs.Enqueue(Math.Max(0, latencyMs));
            while (track.LatenciesMs.Count > 500)
            {
                track.LatenciesMs.Dequeue();
            }
        }
    }

    public void OnStageFailure(string stage)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var track = _stages.GetOrAdd(stage, _ => new StageTrack());
        Interlocked.Increment(ref track.FailureCount);
        Interlocked.Increment(ref _errorCount);
    }

    public void OnError() => Interlocked.Increment(ref _errorCount);

    public MetricsSnapshot Snapshot()
    {
        var totalAudioMs = Interlocked.Read(ref _totalAudioMs);
        var totalAsrMs = Interlocked.Read(ref _totalAsrMs);
        var rtf = totalAudioMs <= 0 ? 0 : (double)totalAsrMs / totalAudioMs;

        var stageStats = new List<StageMetricSnapshot>();
        foreach (var kv in _stages)
        {
            var stage = kv.Key;
            var track = kv.Value;
            var success = Interlocked.Read(ref track.SuccessCount);
            var failure = Interlocked.Read(ref track.FailureCount);
            int p95 = 0;
            int avg = 0;

            lock (_stageLock)
            {
                if (track.LatenciesMs.Count > 0)
                {
                    var sorted = track.LatenciesMs.OrderBy(x => x).ToArray();
                    avg = (int)Math.Round(sorted.Average());
                    var idx = (int)Math.Ceiling(sorted.Length * 0.95) - 1;
                    idx = Math.Clamp(idx, 0, sorted.Length - 1);
                    p95 = sorted[idx];
                }
            }

            var total = success + failure;
            var errorRate = total <= 0 ? 0 : (failure * 100.0) / total;
            stageStats.Add(new StageMetricSnapshot(stage, success, failure, Math.Round(errorRate, 2), avg, p95));
        }

        return new MetricsSnapshot(
            Enabled: _options.Enabled,
            SessionsOpened: Interlocked.Read(ref _sessionsOpened),
            SessionsClosed: Interlocked.Read(ref _sessionsClosed),
            ActiveSessions: _sessions.Count,
            PartialCount: Interlocked.Read(ref _partialCount),
            FinalCount: Interlocked.Read(ref _finalCount),
            InterruptCount: Interlocked.Read(ref _interruptCount),
            ErrorCount: Interlocked.Read(ref _errorCount),
            Rtf: Math.Round(rtf, 4),
            Stages: stageStats.OrderBy(x => x.Stage).ToArray());
    }

    public string SnapshotJson()
    {
        return JsonSerializer.Serialize(Snapshot());
    }
}

public sealed record MetricsSnapshot(
    bool Enabled,
    long SessionsOpened,
    long SessionsClosed,
    int ActiveSessions,
    long PartialCount,
    long FinalCount,
    long InterruptCount,
    long ErrorCount,
    double Rtf,
    IReadOnlyList<StageMetricSnapshot> Stages);

public sealed record StageMetricSnapshot(
    string Stage,
    long SuccessCount,
    long FailureCount,
    double ErrorRatePercent,
    int AvgLatencyMs,
    int P95LatencyMs);
