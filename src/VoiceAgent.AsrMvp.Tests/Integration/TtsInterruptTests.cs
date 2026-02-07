using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class TtsInterruptTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TtsInterruptTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UserSpeechDuringTts_EmitsInterruptEvent()
    {
        var client = _factory.Server.CreateWebSocketClient();
        using var ws = await client.ConnectAsync(new Uri("ws://localhost/ws/stt"), CancellationToken.None);

        const int sampleRate = 16000;
        const int chunkMs = 320;
        var samples = sampleRate * chunkMs / 1000;

        static byte[] MakeFrame(int samples, double amplitude)
        {
            var bytes = new byte[samples * 2];
            for (var i = 0; i < samples; i++)
            {
                var v = amplitude * Math.Sin(2 * Math.PI * 220 * i / 16000.0);
                var s = (short)Math.Clamp((int)(v * 32767), short.MinValue, short.MaxValue);
                bytes[i * 2] = (byte)(s & 0xff);
                bytes[(i * 2) + 1] = (byte)((s >> 8) & 0xff);
            }

            return bytes;
        }

        // Trigger first final + agent + tts.
        for (var i = 0; i < 8; i++)
        {
            await ws.SendAsync(MakeFrame(samples, 0.2), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        for (var i = 0; i < 5; i++)
        {
            await ws.SendAsync(MakeFrame(samples, 0.0), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var sawTtsStart = false;
        var sawInterrupt = false;

        for (var i = 0; i < 80; i++)
        {
            var buffer = new byte[8192];
            var result = await ws.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (text.Contains("\"type\":\"tts\"", StringComparison.Ordinal) &&
                text.Contains("\"state\":\"start\"", StringComparison.Ordinal))
            {
                sawTtsStart = true;

                // Speak immediately while TTS is running.
                await ws.SendAsync(MakeFrame(samples, 0.2), WebSocketMessageType.Binary, true, CancellationToken.None);
                await ws.SendAsync(MakeFrame(samples, 0.2), WebSocketMessageType.Binary, true, CancellationToken.None);
            }

            if (text.Contains("\"type\":\"interrupt\"", StringComparison.Ordinal) &&
                text.Contains("\"state\":\"stop\"", StringComparison.Ordinal))
            {
                sawInterrupt = true;
                break;
            }
        }

        Assert.True(sawTtsStart, "Expected tts.start before interrupt.");
        Assert.True(sawInterrupt, "Expected interrupt.stop when user speaks during TTS.");
    }
}
