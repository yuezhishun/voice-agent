using System.Diagnostics;
using System.Net.WebSockets;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed record DependencyHealthStatus(
    string Stage,
    string Provider,
    bool Healthy,
    long LatencyMs,
    string? Detail = null);

public sealed class DependencyHealthProbe
{
    private readonly AsrMvpOptions _options;

    public DependencyHealthProbe(IOptions<AsrMvpOptions> options)
    {
        _options = options.Value;
    }

    public async Task<IReadOnlyList<DependencyHealthStatus>> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new List<DependencyHealthStatus>(3)
        {
            await CheckAsrAsync(cancellationToken),
            await CheckAgentAsync(cancellationToken),
            await CheckTtsAsync(cancellationToken)
        };

        return checks;
    }

    private async Task<DependencyHealthStatus> CheckAsrAsync(CancellationToken cancellationToken)
    {
        var provider = (_options.AsrProvider ?? "mock").ToLowerInvariant();
        if (provider == "mock")
        {
            return new DependencyHealthStatus("asr", provider, true, 0, "mock provider");
        }

        if (provider == "manyspeech")
        {
            var sw = Stopwatch.StartNew();
            var modelDir = Path.GetFullPath(_options.ManySpeechParaformer.ModelDir);
            var ok = Directory.Exists(modelDir);
            sw.Stop();
            return new DependencyHealthStatus("asr", provider, ok, sw.ElapsedMilliseconds, ok ? modelDir : $"model dir not found: {modelDir}");
        }

        if (provider == "funasr")
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var ws = new ClientWebSocket();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(Math.Max(300, _options.Resilience.Asr.TimeoutMs));
                await ws.ConnectAsync(new Uri(_options.FunAsrWebSocket.Url), timeoutCts.Token);
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "healthz", cancellationToken);
                }

                sw.Stop();
                return new DependencyHealthStatus("asr", provider, true, sw.ElapsedMilliseconds, _options.FunAsrWebSocket.Url);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new DependencyHealthStatus("asr", provider, false, sw.ElapsedMilliseconds, ex.Message);
            }
        }

        return new DependencyHealthStatus("asr", provider, false, 0, "unsupported provider");
    }

    private Task<DependencyHealthStatus> CheckAgentAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var provider = (_options.Agent.Provider ?? "mock").ToLowerInvariant();
        if (provider == "mock")
        {
            return Task.FromResult(new DependencyHealthStatus("agent", provider, true, 0, "mock provider"));
        }

        if (provider is "openai" or "openai-compatible" or "glm")
        {
            var sw = Stopwatch.StartNew();
            var apiKey = _options.Agent.OpenAiCompatible.ApiKey;

            var ok = !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(_options.Agent.OpenAiCompatible.BaseUrl);
            sw.Stop();
            return Task.FromResult(new DependencyHealthStatus(
                "agent",
                provider,
                ok,
                sw.ElapsedMilliseconds,
                ok ? _options.Agent.OpenAiCompatible.BaseUrl : "missing api key or base url"));
        }

        return Task.FromResult(new DependencyHealthStatus("agent", provider, false, 0, "unsupported provider"));
    }

    private Task<DependencyHealthStatus> CheckTtsAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var provider = (_options.Tts.Provider ?? "mock").ToLowerInvariant();
        if (provider == "mock")
        {
            return Task.FromResult(new DependencyHealthStatus("tts", provider, true, 0, "mock provider"));
        }

        if (provider == "kokoro")
        {
            var sw = Stopwatch.StartNew();
            var modelDir = Path.GetFullPath(_options.Tts.Kokoro.ModelDir);
            var required = new[]
            {
                Path.Combine(modelDir, "model.onnx"),
                Path.Combine(modelDir, "voices.bin"),
                Path.Combine(modelDir, "tokens.txt"),
                Path.Combine(modelDir, "espeak-ng-data"),
                Path.Combine(modelDir, "dict")
            };

            var missing = required.Where(x => !File.Exists(x) && !Directory.Exists(x)).ToArray();
            sw.Stop();
            return Task.FromResult(new DependencyHealthStatus(
                "tts",
                provider,
                missing.Length == 0,
                sw.ElapsedMilliseconds,
                missing.Length == 0 ? modelDir : $"missing: {string.Join(',', missing)}"));
        }

        return Task.FromResult(new DependencyHealthStatus("tts", provider, false, 0, "unsupported provider"));
    }
}
