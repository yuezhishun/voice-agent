namespace VoiceAgent.AsrMvp.Contracts;

public sealed record SttMessage(
    string Type,
    string State,
    string Text,
    string SegmentId,
    string SessionId,
    long StartMs,
    long EndMs,
    string? TraceId = null,
    string? FinalReason = null,
    StageLatencyMs? LatencyMs = null);

public sealed record SttErrorMessage(
    string Type,
    string State,
    string SessionId,
    string TraceId,
    ErrorEnvelope Error);

public sealed record ListenControlMessage(
    string Type,
    string State,
    string SessionId);

public sealed record AgentMessage(
    string Type,
    string State,
    string Text,
    string SessionId,
    string SegmentId);

public sealed record TtsMessage(
    string Type,
    string State,
    string SessionId,
    string SegmentId,
    int? SampleRate = null,
    int? Sequence = null,
    string? TraceId = null);

public sealed record InterruptMessage(
    string Type,
    string State,
    string SessionId,
    string SegmentId,
    string Reason,
    long AtMs,
    string? TraceId = null);

public sealed record ErrorEnvelope(
    string Stage,
    string Code,
    string? Detail = null);

public sealed record StageLatencyMs(
    int? Stt = null,
    int? Agent = null,
    int? Tts = null);
