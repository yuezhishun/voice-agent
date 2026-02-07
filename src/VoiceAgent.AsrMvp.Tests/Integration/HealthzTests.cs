using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class HealthzTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthzTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Healthz_ReturnsPerStageChecks()
    {
        using var client = _factory.CreateClient();
        using var resp = await client.GetAsync("/healthz");

        Assert.True(resp.IsSuccessStatusCode || (int)resp.StatusCode == 503);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("status", out var statusNode));
        Assert.True(statusNode.ValueKind == JsonValueKind.String);
        Assert.True(root.TryGetProperty("checks", out var checksNode));
        Assert.Equal(JsonValueKind.Array, checksNode.ValueKind);

        var stages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in checksNode.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("stage", out var stageNode));
            Assert.True(item.TryGetProperty("provider", out _));
            Assert.True(item.TryGetProperty("healthy", out _));
            Assert.True(item.TryGetProperty("latencyMs", out _));
            stages.Add(stageNode.GetString() ?? string.Empty);
        }

        Assert.Contains("asr", stages);
        Assert.Contains("agent", stages);
        Assert.Contains("tts", stages);
    }
}
