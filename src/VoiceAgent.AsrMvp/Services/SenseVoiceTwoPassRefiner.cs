using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class SenseVoiceTwoPassRefiner : ITwoPassRefiner, IDisposable
{
    private sealed class SegmentWindowItem
    {
        public string SegmentId { get; init; } = string.Empty;
        public string OnePassText { get; set; } = string.Empty;
        public float[] Samples { get; init; } = Array.Empty<float>();
        public int DurationMs { get; init; }
    }

    private sealed class SessionWindow
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public List<SegmentWindowItem> Items { get; } = [];
        public string FrozenPrefix { get; set; } = string.Empty;
    }

    private readonly TwoPassOptions _options;
    private readonly OfflineRecognizer _recognizer;
    private readonly SemaphoreSlim _decodeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, SessionWindow> _sessions = new();
    private readonly ILogger<SenseVoiceTwoPassRefiner> _logger;

    public SenseVoiceTwoPassRefiner(IOptions<AsrMvpOptions> options, ILogger<SenseVoiceTwoPassRefiner> logger)
    {
        _logger = logger;
        _options = options.Value.TwoPass;
        var s = _options.SenseVoice;

        var modelDir = Path.GetFullPath(s.ModelDir);
        if (!Directory.Exists(modelDir))
        {
            throw new DirectoryNotFoundException($"SenseVoice model directory not found: {modelDir}");
        }

        var model = ResolveModelPath(modelDir, s.ModelFile);
        var tokens = ResolveTokensPath(modelDir);
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.SenseVoice.Model = model;
        config.ModelConfig.Tokens = tokens;
        config.ModelConfig.ModelType = "sense_voice";
        config.ModelConfig.SenseVoice.Language = s.Language;
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = s.UseInverseTextNormalization ? 1 : 0;
        config.ModelConfig.Provider = s.Provider;
        config.ModelConfig.NumThreads = Math.Max(1, s.NumThreads);
        config.DecodingMethod = "greedy_search";

        _recognizer = new OfflineRecognizer(config);
    }

    public async Task<string> RefineFinalAsync(
        string sessionId,
        string segmentId,
        string onePassFinalText,
        ReadOnlyMemory<float> segmentSamples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(onePassFinalText))
        {
            return onePassFinalText;
        }

        var window = _sessions.GetOrAdd(sessionId, _ => new SessionWindow());
        await window.Lock.WaitAsync(cancellationToken);
        try
        {
            var item = new SegmentWindowItem
            {
                SegmentId = segmentId,
                OnePassText = onePassFinalText,
                Samples = segmentSamples.ToArray(),
                DurationMs = (int)Math.Round(segmentSamples.Length * 1000.0 / Math.Max(1, sampleRate))
            };
            window.Items.Add(item);
            TrimWindow(window);

            var offlineText = await DecodeWindowAsync(window.Items, sampleRate, cancellationToken);
            if (string.IsNullOrWhiteSpace(offlineText))
            {
                return onePassFinalText;
            }

            var prev = window.FrozenPrefix + string.Concat(window.Items.Take(Math.Max(0, window.Items.Count - 1)).Select(x => x.OnePassText));
            var revised = TryExtractCurrentSegmentText(prev, offlineText, onePassFinalText);
            if (string.IsNullOrWhiteSpace(revised))
            {
                return onePassFinalText;
            }

            item.OnePassText = revised;
            return revised;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "2-pass SenseVoice refinement failed; fallback to 1-pass");
            return onePassFinalText;
        }
        finally
        {
            window.Lock.Release();
        }
    }

    public void ResetSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var s))
        {
            s.Lock.Dispose();
        }
    }

    private void TrimWindow(SessionWindow session)
    {
        var maxSeg = Math.Max(1, _options.WindowSegments);
        var maxMs = Math.Max(1000, _options.WindowSeconds * 1000);

        while (session.Items.Count > maxSeg)
        {
            PopAndFreezePrefix(session);
        }

        var totalMs = session.Items.Sum(x => x.DurationMs);
        while (session.Items.Count > 1 && totalMs > maxMs)
        {
            totalMs -= session.Items[0].DurationMs;
            PopAndFreezePrefix(session);
        }
    }

    private void PopAndFreezePrefix(SessionWindow session)
    {
        if (session.Items.Count == 0)
        {
            return;
        }

        var head = session.Items[0];
        session.Items.RemoveAt(0);
        if (_options.PrefixLockEnabled)
        {
            session.FrozenPrefix += head.OnePassText;
        }
    }

    private async Task<string> DecodeWindowAsync(List<SegmentWindowItem> items, int sampleRate, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        var total = items.Sum(x => x.Samples.Length);
        var merged = new float[total];
        var offset = 0;
        foreach (var i in items)
        {
            Array.Copy(i.Samples, 0, merged, offset, i.Samples.Length);
            offset += i.Samples.Length;
        }

        await _decodeLock.WaitAsync(cancellationToken);
        try
        {
            using var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(sampleRate, merged);
            _recognizer.Decode(stream);
            return stream.Result.Text ?? string.Empty;
        }
        finally
        {
            _decodeLock.Release();
        }
    }

    private static string TryExtractCurrentSegmentText(string previousWindowText, string revisedWindowText, string fallback)
    {
        if (string.IsNullOrWhiteSpace(revisedWindowText))
        {
            return fallback;
        }

        if (!string.IsNullOrEmpty(previousWindowText) &&
            revisedWindowText.StartsWith(previousWindowText, StringComparison.Ordinal))
        {
            var tail = revisedWindowText[previousWindowText.Length..].Trim();
            return string.IsNullOrWhiteSpace(tail) ? fallback : tail;
        }

        // Fallback: preserve a stable prefix and replace tail only.
        var common = LongestCommonPrefix(previousWindowText + fallback, revisedWindowText);
        var suffix = revisedWindowText.Length > common ? revisedWindowText[common..] : revisedWindowText;
        suffix = suffix.Trim();
        return string.IsNullOrWhiteSpace(suffix) ? fallback : suffix;
    }

    private static int LongestCommonPrefix(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i])
        {
            i++;
        }

        return i;
    }

    private static string ResolveModelPath(string modelDir, string modelFile)
    {
        if (Path.IsPathRooted(modelFile) && File.Exists(modelFile))
        {
            return modelFile;
        }

        var inDir = Path.Combine(modelDir, modelFile);
        if (File.Exists(inDir))
        {
            return inDir;
        }

        var candidates = Directory.EnumerateFiles(modelDir, "model*.onnx", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x)
            .ToArray();
        if (candidates.Length > 0)
        {
            return candidates[0];
        }

        throw new FileNotFoundException($"SenseVoice model file not found in {modelDir}. expected: {modelFile}");
    }

    private static string ResolveTokensPath(string modelDir)
    {
        var direct = Path.Combine(modelDir, "tokens.txt");
        if (File.Exists(direct))
        {
            return direct;
        }

        var any = Directory.EnumerateFiles(modelDir, "*token*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(any))
        {
            return any;
        }

        throw new FileNotFoundException($"SenseVoice tokens file not found in {modelDir}");
    }

    public void Dispose()
    {
        foreach (var kv in _sessions)
        {
            kv.Value.Lock.Dispose();
        }

        _sessions.Clear();
        _decodeLock.Dispose();
        _recognizer.Dispose();
    }
}
