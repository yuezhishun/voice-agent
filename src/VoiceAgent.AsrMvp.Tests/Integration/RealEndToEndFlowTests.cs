using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using VoiceAgent.AsrMvp.Utils;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class RealEndToEndFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RealEndToEndFlowTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RealManySpeechGlmKokoro_CanCompleteEndToEndFlow_WhenConfigured()
    {
        var settings = RealIntegrationTestSettings.Load();
        if (!settings.Enabled)
        {
            return;
        }

        var wavPath = settings.E2eWavFile;
        if (string.IsNullOrWhiteSpace(wavPath))
        {
            return;
        }

        if (!File.Exists(wavPath))
        {
            throw new FileNotFoundException($"RealIntegration:E2eWavFile not found: {wavPath}");
        }

        if (string.IsNullOrWhiteSpace(settings.Glm.ApiKey))
        {
            throw new InvalidOperationException("RealIntegration:Glm:ApiKey is required for RealEndToEndFlowTests.");
        }

        var paraformerDir = settings.ParaformerModelDir;
        var kokoroDir = settings.KokoroModelDir;

        if (!Directory.Exists(paraformerDir))
        {
            throw new DirectoryNotFoundException($"RealIntegration:ParaformerModelDir not found: {paraformerDir}");
        }

        if (!Directory.Exists(kokoroDir))
        {
            throw new DirectoryNotFoundException($"RealIntegration:KokoroModelDir not found: {kokoroDir}");
        }

        var cfg = new Dictionary<string, string?>
        {
            ["AsrMvp:AsrProvider"] = "manyspeech",
            ["AsrMvp:ManySpeechParaformer:ModelDir"] = paraformerDir,
            ["AsrMvp:Agent:Provider"] = "openai",
            ["AsrMvp:Agent:OpenAiCompatible:BaseUrl"] = settings.Glm.BaseUrl,
            ["AsrMvp:Agent:OpenAiCompatible:ApiKey"] = settings.Glm.ApiKey,
            ["AsrMvp:Agent:OpenAiCompatible:Model"] = settings.Glm.Model,
            ["AsrMvp:Agent:OpenAiCompatible:Temperature"] = "0.2",
            ["AsrMvp:Agent:TimeoutMs"] = "20000",
            ["AsrMvp:Tts:Provider"] = "kokoro",
            ["AsrMvp:Tts:SampleRate"] = "24000",
            ["AsrMvp:Tts:TimeoutMs"] = "20000",
            ["AsrMvp:Tts:Kokoro:ModelDir"] = kokoroDir,
            ["AsrMvp:Tts:Kokoro:Lang"] = settings.KokoroLang,
            ["AsrMvp:Tts:Kokoro:Lexicon"] = settings.KokoroLexicon
        };

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(cfg);
            });
        });

        var (samples, sampleRate, channels) = WavFileReader.ReadPcm16(wavPath);
        if (sampleRate != 16000)
        {
            throw new InvalidDataException($"RealIntegration:E2eWavFile must be 16k PCM16. Actual sample rate: {sampleRate}");
        }

        var mono = ToMono(samples, channels);
        var wsClient = factory.Server.CreateWebSocketClient();
        using var ws = await wsClient.ConnectAsync(new Uri("ws://localhost/ws/stt"), CancellationToken.None);

        const int chunkMs = 320;
        var chunkSamples = sampleRate * chunkMs / 1000;
        for (var offset = 0; offset < mono.Length; offset += chunkSamples)
        {
            var len = Math.Min(chunkSamples, mono.Length - offset);
            var payload = ToPcm16Bytes(mono.AsSpan(offset, len));
            await ws.SendAsync(payload, WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        // Force finalization so the test doesn't depend on endpointing timing.
        var stop = Encoding.UTF8.GetBytes("""{"type":"listen","state":"stop"}""");
        await ws.SendAsync(stop, WebSocketMessageType.Text, true, CancellationToken.None);

        var finalCount = 0;
        var agentCount = 0;
        var ttsStart = 0;
        var ttsStop = 0;
        var ttsBinary = 0;
        var finalTexts = new List<string>();
        var agentTexts = new List<string>();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        for (var i = 0; i < 300; i++)
        {
            var buffer = new byte[64 * 1024];
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                ttsBinary++;
                if (finalCount > 0 && agentCount > 0 && ttsStart > 0 && ttsStop > 0)
                {
                    break;
                }

                continue;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (!TryReadMessage(text, out var type, out var state, out var payloadText))
            {
                continue;
            }

            if (type == "stt" && state == "final")
            {
                finalCount++;
                if (!string.IsNullOrWhiteSpace(payloadText))
                {
                    finalTexts.Add(payloadText);
                }
            }
            else if (type == "agent" && state == "response")
            {
                agentCount++;
                if (!string.IsNullOrWhiteSpace(payloadText))
                {
                    agentTexts.Add(payloadText);
                }
            }
            else if (type == "tts" && state == "start")
            {
                ttsStart++;
            }
            else if (type == "tts" && state == "stop")
            {
                ttsStop++;
            }

            if (finalCount > 0 && agentCount > 0 && ttsStart > 0 && ttsStop > 0 && ttsBinary > 0)
            {
                break;
            }
        }

        Assert.True(finalCount > 0, "Expected at least one stt.final message.");
        Assert.True(finalTexts.Any(x => !string.IsNullOrWhiteSpace(x)), "Expected non-empty stt.final text.");
        Assert.True(agentCount > 0, "Expected at least one agent.response message.");
        Assert.True(agentTexts.Any(x => !string.IsNullOrWhiteSpace(x)), "Expected non-empty agent.response text.");
        Assert.True(ttsStart > 0, "Expected tts.start message.");
        Assert.True(ttsBinary > 0, "Expected at least one binary tts chunk.");
        Assert.True(ttsStop > 0, "Expected tts.stop message.");
    }

    private static float[] ToMono(float[] samples, int channels)
    {
        if (channels <= 1)
        {
            return samples;
        }

        var mono = new float[samples.Length / channels];
        for (var i = 0; i < mono.Length; i++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += samples[(i * channels) + c];
            }

            mono[i] = (float)(sum / channels);
        }

        return mono;
    }

    private static byte[] ToPcm16Bytes(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var scaled = (int)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            var pcm = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(pcm & 0xff);
            bytes[(i * 2) + 1] = (byte)((pcm >> 8) & 0xff);
        }

        return bytes;
    }

    private static bool TryReadMessage(string json, out string type, out string state, out string text)
    {
        type = string.Empty;
        state = string.Empty;
        text = string.Empty;

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

            return !string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(state);
        }
        catch
        {
            return false;
        }
    }
}
