using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VoiceAgent.AsrMvp.Domain;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class EndToEndScenarioMatrixTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EndToEndScenarioMatrixTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HappyPath_EmitsSttFinal_AgentResponse_AndTts()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AsrMvp:Release:ForceMockAsr"] = "true",
                    ["AsrMvp:Release:ForceMockAgent"] = "true",
                    ["AsrMvp:Release:ForceMockTts"] = "true",
                    ["Logging:LogLevel:Default"] = "Error",
                    ["Logging:LogLevel:SttSocket"] = "Error"
                });
            });
        });

        using var ws = await ConnectWebSocketAsync(factory);
        await SendSpeechThenSilenceAsync(ws);

        var observed = await ObserveAsync(ws, TimeSpan.FromSeconds(8));

        Assert.True(observed.PartialCount > 0, "Expected at least one stt.partial.");
        Assert.True(observed.FinalCount > 0, "Expected at least one stt.final.");
        Assert.True(observed.FinalReasonSeen, "Expected finalReason endpointing|max_segment|listen_stop.");
        Assert.True(observed.AgentResponseCount > 0, "Expected at least one agent.response.");
        Assert.True(observed.TtsStartCount > 0, "Expected tts.start.");
        Assert.True(observed.TtsBinaryCount > 0, "Expected binary TTS chunks.");
        Assert.True(observed.TtsStopCount > 0, "Expected tts.stop.");
    }

    [Fact]
    public async Task AsrException_EmitsStandardAsrError()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Default"] = "Error",
                    ["Logging:LogLevel:SttSocket"] = "Error"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStreamingAsrEngine, ThrowingAsrEngine>();
            });
        });

        using var ws = await ConnectWebSocketAsync(factory);
        await SendSpeechFramesAsync(ws, count: 3, amplitude: 0.2);

        var observed = await ObserveAsync(ws, TimeSpan.FromSeconds(5));

        Assert.Contains(observed.Errors, e => e.Stage == "asr" && e.Code == "ASR_PROVIDER_ERROR");
    }

    [Fact]
    public async Task AgentException_FallsBackAndStillRunsTts()
    {
        const string fallbackText = "[fallback] agent unavailable";

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AsrMvp:Fallback:EnableOnAgentFailure"] = "true",
                    ["AsrMvp:Fallback:AgentFallbackText"] = fallbackText,
                    ["Logging:LogLevel:Default"] = "Error",
                    ["Logging:LogLevel:SttSocket"] = "Error"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAgentEngine, ThrowingAgentEngine>();
            });
        });

        using var ws = await ConnectWebSocketAsync(factory);
        await SendSpeechThenSilenceAsync(ws);

        var observed = await ObserveAsync(ws, TimeSpan.FromSeconds(8));

        Assert.True(observed.AgentResponseTexts.Any(t => string.Equals(t, fallbackText, StringComparison.Ordinal)),
            "Expected fallback agent response text.");
        Assert.True(observed.TtsStartCount > 0, "Expected TTS to still run after agent fallback.");
        Assert.True(observed.TtsBinaryCount > 0, "Expected binary TTS chunks after fallback.");
    }

    [Fact]
    public async Task TtsException_EmitsStandardTtsError()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Default"] = "Error",
                    ["Logging:LogLevel:SttSocket"] = "Error"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ITtsEngine, ThrowingTtsEngine>();
            });
        });

        using var ws = await ConnectWebSocketAsync(factory);
        await SendSpeechThenSilenceAsync(ws);

        var observed = await ObserveAsync(ws, TimeSpan.FromSeconds(8));

        Assert.True(observed.TtsStartCount > 0, "Expected tts.start before TTS error.");
        Assert.Contains(observed.Errors, e => e.Stage == "tts" && e.Code == "TTS_PROVIDER_ERROR");
    }

    private static async Task<WebSocket> ConnectWebSocketAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.Server.CreateWebSocketClient();
        var ws = await client.ConnectAsync(new Uri("ws://localhost/ws/stt"), CancellationToken.None);
        return ws;
    }

    private static async Task SendSpeechThenSilenceAsync(WebSocket ws)
    {
        await SendSpeechFramesAsync(ws, count: 8, amplitude: 0.2);
        await SendSpeechFramesAsync(ws, count: 5, amplitude: 0.0);
    }

    private static async Task SendSpeechFramesAsync(WebSocket ws, int count, double amplitude)
    {
        const int sampleRate = 16000;
        const int chunkMs = 320;
        var samples = sampleRate * chunkMs / 1000;

        for (var i = 0; i < count; i++)
        {
            await ws.SendAsync(MakeSineFrame(samples, amplitude), WebSocketMessageType.Binary, true, CancellationToken.None);
        }
    }

    private static byte[] MakeSineFrame(int samples, double amplitude)
    {
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var v = amplitude * Math.Sin(2 * Math.PI * 220 * i / 16000.0);
            var s = (short)Math.Clamp((int)(v * short.MaxValue), short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xff);
            bytes[(i * 2) + 1] = (byte)((s >> 8) & 0xff);
        }

        return bytes;
    }

    private static async Task<ObservedEvents> ObserveAsync(WebSocket ws, TimeSpan timeout)
    {
        var observed = new ObservedEvents();

        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            var buffer = new byte[64 * 1024];
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                observed.TtsBinaryCount++;
                continue;
            }

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (!TryParse(json, out var type, out var state, out var text, out var finalReason, out var error))
            {
                continue;
            }

            if (type == "stt" && state == "partial")
            {
                observed.PartialCount++;
            }
            else if (type == "stt" && state == "final")
            {
                observed.FinalCount++;
                if (finalReason is "endpointing" or "max_segment" or "listen_stop")
                {
                    observed.FinalReasonSeen = true;
                }
            }
            else if (type == "agent" && state == "response")
            {
                observed.AgentResponseCount++;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    observed.AgentResponseTexts.Add(text);
                }
            }
            else if (type == "tts" && state == "start")
            {
                observed.TtsStartCount++;
            }
            else if (type == "tts" && state == "stop")
            {
                observed.TtsStopCount++;
            }
            else if (type == "interrupt" && state == "stop")
            {
                observed.InterruptCount++;
            }
            else if (type == "stt" && state == "error" && error is not null)
            {
                observed.Errors.Add(error.Value);
            }

            if (observed.FinalCount > 0 && observed.AgentResponseCount > 0 && observed.TtsStartCount > 0 &&
                (observed.TtsStopCount > 0 || observed.Errors.Any(e => e.Stage == "tts")))
            {
                break;
            }
        }

        return observed;
    }

    private static bool TryParse(
        string json,
        out string type,
        out string state,
        out string text,
        out string finalReason,
        out ErrorInfo? error)
    {
        type = string.Empty;
        state = string.Empty;
        text = string.Empty;
        finalReason = string.Empty;
        error = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeNode) && typeNode.ValueKind == JsonValueKind.String)
            {
                type = typeNode.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("state", out var stateNode) && stateNode.ValueKind == JsonValueKind.String)
            {
                state = stateNode.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String)
            {
                text = textNode.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("finalReason", out var reasonNode) && reasonNode.ValueKind == JsonValueKind.String)
            {
                finalReason = reasonNode.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("error", out var errorNode) && errorNode.ValueKind == JsonValueKind.Object)
            {
                var stage = errorNode.TryGetProperty("stage", out var stageNode) && stageNode.ValueKind == JsonValueKind.String
                    ? stageNode.GetString() ?? string.Empty
                    : string.Empty;
                var code = errorNode.TryGetProperty("code", out var codeNode) && codeNode.ValueKind == JsonValueKind.String
                    ? codeNode.GetString() ?? string.Empty
                    : string.Empty;
                error = new ErrorInfo(stage, code);
            }

            return !string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(state);
        }
        catch
        {
            return false;
        }
    }

    private sealed class ThrowingAsrEngine : IStreamingAsrEngine
    {
        public Task<string> DecodePartialAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
            => throw new InvalidOperationException("synthetic_asr_failure");

        public Task<string> DecodeFinalAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
            => throw new InvalidOperationException("synthetic_asr_failure");
    }

    private sealed class ThrowingAgentEngine : IAgentEngine
    {
        public Task<string> GenerateReplyAsync(
            string sessionId,
            string systemPrompt,
            IReadOnlyList<AgentTurn> history,
            string userText,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("synthetic_agent_failure");
    }

    private sealed class ThrowingTtsEngine : ITtsEngine
    {
        public IAsyncEnumerable<byte[]> SynthesizePcm16Async(
            string sessionId,
            string text,
            int sampleRate,
            int chunkDurationMs,
            CancellationToken cancellationToken)
            => Throw();

        private static async IAsyncEnumerable<byte[]> Throw([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("synthetic_tts_failure");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class ObservedEvents
    {
        public int PartialCount { get; set; }
        public int FinalCount { get; set; }
        public bool FinalReasonSeen { get; set; }
        public int AgentResponseCount { get; set; }
        public int TtsStartCount { get; set; }
        public int TtsStopCount { get; set; }
        public int TtsBinaryCount { get; set; }
        public int InterruptCount { get; set; }
        public List<string> AgentResponseTexts { get; } = new();
        public List<ErrorInfo> Errors { get; } = new();
    }

    private readonly record struct ErrorInfo(string Stage, string Code);
}
