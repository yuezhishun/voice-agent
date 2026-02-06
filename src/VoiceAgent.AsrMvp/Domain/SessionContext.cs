using System.Collections.Concurrent;
using System.Threading;

namespace VoiceAgent.AsrMvp.Domain;

public sealed class SessionContext
{
    private long _segmentSeq;

    public SessionContext(string sessionId)
    {
        SessionId = sessionId;
    }

    public string SessionId { get; }
    public EndpointingState Endpointing { get; } = new();
    public TranscriptState Transcript { get; } = new();
    public string? ActiveSegmentId { get; set; }

    public ConcurrentQueue<float> SegmentBuffer { get; } = new();

    public void AppendSamples(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            SegmentBuffer.Enqueue(sample);
        }
    }

    public float[] DrainSegment()
    {
        var list = new List<float>(SegmentBuffer.Count);
        while (SegmentBuffer.TryDequeue(out var sample))
        {
            list.Add(sample);
        }

        return list.ToArray();
    }

    public float[] SnapshotSegment()
    {
        return SegmentBuffer.ToArray();
    }

    public string NextSegmentId()
    {
        var seq = Interlocked.Increment(ref _segmentSeq);
        return $"seg-{seq}";
    }
}
