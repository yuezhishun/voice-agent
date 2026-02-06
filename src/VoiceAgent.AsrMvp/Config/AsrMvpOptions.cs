namespace VoiceAgent.AsrMvp.Config;

public sealed class AsrMvpOptions
{
    public const string SectionName = "AsrMvp";

    public string AsrProvider { get; set; } = "mock";
    public int SampleRate { get; set; } = 16000;
    public int ChunkMs { get; set; } = 320;
    public int TailRollbackSeconds { get; set; } = 2;
    public FunAsrWebSocketOptions FunAsrWebSocket { get; set; } = new();
    public ManySpeechParaformerOptions ManySpeechParaformer { get; set; } = new();
    public EndpointingOptions Endpointing { get; set; } = new();
    public AgentOptions Agent { get; set; } = new();
    public TtsOptions Tts { get; set; } = new();
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
    public int SampleRate { get; set; } = 16000;
    public int TimeoutMs { get; set; } = 3000;
    public int ChunkDurationMs { get; set; } = 200;
}
