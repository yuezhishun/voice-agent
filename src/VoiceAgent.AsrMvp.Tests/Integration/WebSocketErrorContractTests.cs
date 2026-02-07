using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class WebSocketErrorContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebSocketErrorContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InvalidPcmFrame_EmitsStandardizedErrorEnvelope()
    {
        var client = _factory.Server.CreateWebSocketClient();
        using var ws = await client.ConnectAsync(new Uri("ws://localhost/ws/stt"), CancellationToken.None);

        // Send invalid PCM16 payload (odd length) to trigger decode failure.
        var invalidPayload = new byte[] { 0x01 };
        await ws.SendAsync(invalidPayload, WebSocketMessageType.Binary, true, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[8192];
        var result = await ws.ReceiveAsync(buffer, timeout.Token);

        Assert.Equal(WebSocketMessageType.Text, result.MessageType);

        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("stt", root.GetProperty("type").GetString());
        Assert.Equal("error", root.GetProperty("state").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("sessionId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));

        var error = root.GetProperty("error");
        Assert.Equal("decode", error.GetProperty("stage").GetString());
        Assert.Equal("DECODE_FAIL", error.GetProperty("code").GetString());
        Assert.Equal("Invalid PCM16 payload", error.GetProperty("detail").GetString());
    }
}
