using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class AlertsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AlertsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AlertsEndpoint_ReturnsContract()
    {
        using var client = _factory.CreateClient();
        using var resp = await client.GetAsync("/alerts");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("status", out var statusNode));
        Assert.True(statusNode.ValueKind == JsonValueKind.String);
        Assert.True(root.TryGetProperty("generatedAt", out _));
        Assert.True(root.TryGetProperty("alerts", out var alertsNode));
        Assert.Equal(JsonValueKind.Array, alertsNode.ValueKind);
    }

    [Fact]
    public async Task ReleaseEndpoint_ReturnsEffectiveProviders()
    {
        using var client = _factory.CreateClient();
        using var resp = await client.GetAsync("/releasez");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("version", out _));
        Assert.True(root.TryGetProperty("runtimeProfile", out _));
        Assert.True(root.TryGetProperty("asrProvider", out _));
        Assert.True(root.TryGetProperty("agentProvider", out _));
        Assert.True(root.TryGetProperty("ttsProvider", out _));
    }
}
