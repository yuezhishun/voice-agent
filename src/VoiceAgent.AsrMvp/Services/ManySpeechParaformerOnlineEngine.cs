using System.Collections.Concurrent;
using System.Text.Json;
using ManySpeech.AliParaformerAsr;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class ManySpeechParaformerOnlineEngine : IStreamingAsrEngine, IDisposable
{
    private sealed class StreamState
    {
        public OnlineStream Stream { get; init; } = default!;
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public int SentSamples { get; set; }
        public string LastText { get; set; } = string.Empty;
    }

    private readonly OnlineRecognizer _recognizer;
    private readonly ConcurrentDictionary<string, StreamState> _streams = new();

    public ManySpeechParaformerOnlineEngine(IOptions<AsrMvpOptions> options)
    {
        var opt = options.Value.ManySpeechParaformer;
        var modelDir = Path.GetFullPath(opt.ModelDir);
        if (!Directory.Exists(modelDir))
        {
            throw new DirectoryNotFoundException($"ManySpeech paraformer model directory not found: {modelDir}");
        }

        var encoder = ResolveModelFile(modelDir, ["encoder*.onnx", "model*.onnx"]);
        var decoder = ResolveModelFile(modelDir, ["decoder*.onnx"]);
        var config = ResolveModelFile(modelDir, ["asr*.yaml", "config*.yaml", "configuration*.json"]);
        var mvn = ResolveModelFile(modelDir, ["am*.mvn"]);
        var tokens = ResolveTokensFile(modelDir, opt.AutoGenerateTokensTxt);

        _recognizer = new OnlineRecognizer(encoder, decoder, config, mvn, tokens, opt.Threads);
    }

    public async Task<string> DecodePartialAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        var state = _streams.GetOrAdd(sessionId, _ => new StreamState { Stream = _recognizer.CreateOnlineStream() });

        await state.Lock.WaitAsync(cancellationToken);
        try
        {
            AddDelta(state, samples);
            var result = _recognizer.GetResult(state.Stream);
            state.LastText = result?.Text ?? state.LastText;
            return state.LastText;
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public async Task<string> DecodeFinalAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        var state = _streams.GetOrAdd(sessionId, _ => new StreamState { Stream = _recognizer.CreateOnlineStream() });

        await state.Lock.WaitAsync(cancellationToken);
        try
        {
            AddDelta(state, samples);

            // Flush remaining internal buffers.
            for (var i = 0; i < 8 && !state.Stream.IsFinished(true); i++)
            {
                _ = _recognizer.GetResult(state.Stream);
            }

            var result = _recognizer.GetResult(state.Stream);
            state.LastText = result?.Text ?? state.LastText;
            return state.LastText;
        }
        finally
        {
            state.Lock.Release();
            if (_streams.TryRemove(sessionId, out var removed))
            {
                removed.Stream.Dispose();
                removed.Lock.Dispose();
            }
        }
    }

    private static void AddDelta(StreamState state, ReadOnlyMemory<float> samples)
    {
        if (samples.Length <= state.SentSamples)
        {
            return;
        }

        var delta = samples.Span[state.SentSamples..].ToArray();
        if (delta.Length > 0)
        {
            state.Stream.AddSamples(delta);
            state.SentSamples = samples.Length;
        }
    }

    private static string ResolveModelFile(string dir, IEnumerable<string> patterns)
    {
        foreach (var p in patterns)
        {
            var match = Directory.EnumerateFiles(dir, p, SearchOption.TopDirectoryOnly)
                .OrderBy(x => x)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        throw new FileNotFoundException($"No model file matched patterns: {string.Join(',', patterns)} in {dir}");
    }

    private static string ResolveTokensFile(string dir, bool autoGenerate)
    {
        var txt = Directory.EnumerateFiles(dir, "tokens*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(txt))
        {
            return txt;
        }

        var json = Directory.EnumerateFiles(dir, "tokens*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new FileNotFoundException($"tokens.txt/tokens.json not found in {dir}");
        }

        if (!autoGenerate)
        {
            throw new FileNotFoundException($"tokens.txt missing and auto generation disabled. json: {json}");
        }

        var txtPath = Path.Combine(dir, "tokens.txt");
        var tokens = ParseTokens(json);
        File.WriteAllLines(txtPath, tokens);
        return txtPath;
    }

    private static IReadOnlyList<string> ParseTokens(string jsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement.EnumerateArray()
                .Select(x => x.GetString() ?? string.Empty)
                .ToArray();
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            return doc.RootElement.EnumerateObject()
                .OrderBy(x => int.TryParse(x.Value.ToString(), out var i) ? i : int.MaxValue)
                .Select(x => x.Name)
                .ToArray();
        }

        throw new InvalidDataException($"Unsupported tokens.json format: {jsonPath}");
    }

    public void Dispose()
    {
        foreach (var kv in _streams)
        {
            kv.Value.Stream.Dispose();
            kv.Value.Lock.Dispose();
        }

        _streams.Clear();
        _recognizer.Dispose();
    }
}
