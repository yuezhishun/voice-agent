using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class WebSocketFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebSocketFlowTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EmitsPartialThenFinalForSpeechFollowedBySilence()
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

        for (var i = 0; i < 6; i++)
        {
            await ws.SendAsync(MakeFrame(samples, 0.2), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        for (var i = 0; i < 5; i++)
        {
            await ws.SendAsync(MakeFrame(samples, 0.0), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        var partial = 0;
        var final = 0;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        for (var i = 0; i < 40; i++)
        {
            var buffer = new byte[8192];
            var result = await ws.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (text.Contains("\"state\":\"partial\"", StringComparison.Ordinal))
            {
                partial++;
            }

            if (text.Contains("\"state\":\"final\"", StringComparison.Ordinal))
            {
                final++;
                break;
            }
        }

        Assert.True(partial >= 1, "Expected at least one partial message.");
        Assert.True(final >= 1, "Expected one final message.");
    }
}
