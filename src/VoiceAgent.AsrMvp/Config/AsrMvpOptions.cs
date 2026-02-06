namespace VoiceAgent.AsrMvp.Config;

public sealed class AsrMvpOptions
{
    public const string SectionName = "AsrMvp";

    public string AsrProvider { get; set; } = "mock";
    public int SampleRate { get; set; } = 16000;
    public int ChunkMs { get; set; } = 320;
    public int TailRollbackSeconds { get; set; } = 2;
    public AudioProcessingOptions AudioProcessing { get; set; } = new();
    public FunAsrWebSocketOptions FunAsrWebSocket { get; set; } = new();
    public ManySpeechParaformerOptions ManySpeechParaformer { get; set; } = new();
    public EndpointingOptions Endpointing { get; set; } = new();
    public TwoPassOptions TwoPass { get; set; } = new();
    public PostProcessOptions PostProcess { get; set; } = new();
    public MetricsOptions Metrics { get; set; } = new();
    public AgentOptions Agent { get; set; } = new();
    public TtsOptions Tts { get; set; } = new();
}

public sealed class TwoPassOptions
{
    public bool Enabled { get; set; } = false;
    public string Provider { get; set; } = "sensevoice";
    public int WindowSeconds { get; set; } = 12;
    public int WindowSegments { get; set; } = 3;
    public bool PrefixLockEnabled { get; set; } = true;
    public SenseVoiceOfflineOptions SenseVoice { get; set; } = new();
}

public sealed class SenseVoiceOfflineOptions
{
    public string ModelDir { get; set; } = "models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17";
    public string ModelFile { get; set; } = "model.int8.onnx";
    public string Language { get; set; } = "zh";
    public bool UseInverseTextNormalization { get; set; } = true;
    public int NumThreads { get; set; } = 2;
    public string Provider { get; set; } = "cpu";
}

public sealed class AudioProcessingOptions
{
    public bool EnableDenoise { get; set; } = true;
    public bool EnableAgc { get; set; } = true;
    public bool EnableDcRemoval { get; set; } = true;
    public bool EnablePreEmphasis { get; set; } = true;
    public float AgcTargetRms { get; set; } = 0.12f;
    public float AgcMaxGain { get; set; } = 6.0f;
    public float NoiseFloorAlpha { get; set; } = 0.98f;
    public float NoiseSuppressStrength { get; set; } = 0.65f;
    public float PreEmphasis { get; set; } = 0.97f;
}

public sealed class FunAsrWebSocketOptions
{
    public string Url { get; set; } = "ws://127.0.0.1:10095";
    public string Mode { get; set; } = "online";
    public int[] ChunkSize { get; set; } = [5, 10, 5];
    public int ChunkInterval { get; set; } = 10;
    public int EncoderChunkLookBack { get; set; } = 4;
    public int DecoderChunkLookBack { get; set; } = 0;
    public bool Itn { get; set; } = true;
    public int ReceiveTimeoutMs { get; set; } = 300;
    public int FinalTimeoutMs { get; set; } = 3000;
}

public sealed class ManySpeechParaformerOptions
{
    public string ModelDir { get; set; } = "models/paraformer-online-onnx";
    public int Threads { get; set; } = 2;
    public bool AutoGenerateTokensTxt { get; set; } = true;
}

public sealed class EndpointingOptions
{
    public int EndSilenceMs { get; set; } = 800;
    public int MinSegmentMs { get; set; } = 1200;
    public int MinSilenceMs { get; set; } = 300;
    public int MergeBackMs { get; set; } = 300;
    public int MaxSegmentMs { get; set; } = 15000;
    public float EnergyThreshold { get; set; } = 0.012f;
    public bool AdaptiveVadEnabled { get; set; } = true;
    public float AdaptiveVadFloor { get; set; } = 0.004f;
    public float AdaptiveVadMultiplier { get; set; } = 2.2f;
    public int AdaptiveVadWindowFrames { get; set; } = 40;
}

public sealed class PostProcessOptions
{
    public bool EnablePunctuation { get; set; } = true;
    public bool EnableNormalization { get; set; } = true;
}

public sealed class MetricsOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class AgentOptions
{
    public string Provider { get; set; } = "mock";
    public int MaxHistoryTurns { get; set; } = 8;
    public int TimeoutMs { get; set; } = 3000;
    public string SystemPrompt { get; set; } = "You are a concise helpful assistant for voice conversation.";
    public OpenAiCompatibleAgentOptions OpenAiCompatible { get; set; } = new();
}

public sealed class OpenAiCompatibleAgentOptions
{
    public string BaseUrl { get; set; } = "https://open.bigmodel.cn/api/paas/v4/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "glm-4.7";
    public float Temperature { get; set; } = 0.7f;
    public int? MaxTokens { get; set; }
}

public sealed class TtsOptions
{
    public string Provider { get; set; } = "mock";
    public int SampleRate { get; set; } = 16000;
    public int TimeoutMs { get; set; } = 3000;
    public int ChunkDurationMs { get; set; } = 200;
    public KokoroTtsOptions Kokoro { get; set; } = new();
}

public sealed class KokoroTtsOptions
{
    public string ModelDir { get; set; } = "models/kokoro-v1.0";
    public string? Lexicon { get; set; }
    public string Lang { get; set; } = "zh";
    public string Provider { get; set; } = "cpu";
    public int NumThreads { get; set; } = 2;
    public float Speed { get; set; } = 1.0f;
    public int SpeakerId { get; set; } = 50;
}
