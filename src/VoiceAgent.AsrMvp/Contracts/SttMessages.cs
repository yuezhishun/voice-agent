namespace VoiceAgent.AsrMvp.Contracts;

public sealed record SttMessage(
    string Type,
    string State,
    string Text,
    string SegmentId,
    string SessionId,
    long StartMs,
    long EndMs);

public sealed record SttErrorMessage(
    string Type,
    string State,
    string Code,
    string SessionId,
    string? Detail = null);

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
    int? Sequence = null);
