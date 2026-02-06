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

    private readonly MetricsOptions _options;
    private readonly ConcurrentDictionary<string, SessionTrack> _sessions = new();

    private long _sessionsOpened;
    private long _sessionsClosed;
    private long _partialCount;
    private long _finalCount;
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

    public void OnError() => Interlocked.Increment(ref _errorCount);

    public string SnapshotJson()
    {
        var totalAudioMs = Interlocked.Read(ref _totalAudioMs);
        var totalAsrMs = Interlocked.Read(ref _totalAsrMs);
        var rtf = totalAudioMs <= 0 ? 0 : (double)totalAsrMs / totalAudioMs;
        var payload = new
        {
            enabled = _options.Enabled,
            sessionsOpened = Interlocked.Read(ref _sessionsOpened),
            sessionsClosed = Interlocked.Read(ref _sessionsClosed),
            activeSessions = _sessions.Count,
            partialCount = Interlocked.Read(ref _partialCount),
            finalCount = Interlocked.Read(ref _finalCount),
            errorCount = Interlocked.Read(ref _errorCount),
            rtf = Math.Round(rtf, 4)
        };
        return JsonSerializer.Serialize(payload);
    }
}
