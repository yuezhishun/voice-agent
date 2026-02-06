using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class KokoroTtsEngine : ITtsEngine, IDisposable
{
    private readonly SemaphoreSlim _synthesisLock = new(1, 1);
    private readonly OfflineTts _tts;
    private readonly KokoroTtsOptions _options;
    private readonly ILogger<KokoroTtsEngine> _logger;

    public KokoroTtsEngine(IOptions<AsrMvpOptions> options, ILogger<KokoroTtsEngine> logger)
    {
        _logger = logger;
        _options = options.Value.Tts.Kokoro;

        var modelDir = Path.GetFullPath(_options.ModelDir);
        if (!Directory.Exists(modelDir))
        {
            throw new DirectoryNotFoundException($"Kokoro model directory not found: {modelDir}");
        }

        var model = ResolvePath(modelDir, "model.onnx");
        var voices = ResolvePath(modelDir, "voices.bin");
        var tokens = ResolvePath(modelDir, "tokens.txt");
        var dataDir = ResolvePath(modelDir, "espeak-ng-data");
        var dictDir = ResolvePath(modelDir, "dict");

        var config = new OfflineTtsConfig();
        config.Model.NumThreads = Math.Max(1, _options.NumThreads);
        config.Model.Provider = _options.Provider;
        config.Model.Kokoro.Model = model;
        config.Model.Kokoro.Voices = voices;
        config.Model.Kokoro.Tokens = tokens;
        config.Model.Kokoro.DataDir = dataDir;
        config.Model.Kokoro.DictDir = dictDir;
        config.Model.Kokoro.Lang = string.IsNullOrWhiteSpace(_options.Lang) ? "zh" : _options.Lang;
        config.Model.Kokoro.Lexicon = ResolveLexicon(modelDir, _options.Lexicon);

        _tts = new OfflineTts(config);
    }

    public async IAsyncEnumerable<byte[]> SynthesizePcm16Async(
        string sessionId,
        string text,
        int sampleRate,
        int chunkDurationMs,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        await _synthesisLock.WaitAsync(cancellationToken);
        try
        {
            var ttsSampleRate = _tts.SampleRate;
            if (sampleRate != ttsSampleRate)
            {
                _logger.LogWarning(
                    "Kokoro output sample rate {TtsSampleRate} differs from requested {RequestedSampleRate}; output is not resampled",
                    ttsSampleRate,
                    sampleRate);
            }

            var targetSamples = Math.Max(1, ttsSampleRate * Math.Max(1, chunkDurationMs) / 1000);
            var targetBytes = targetSamples * sizeof(short);

            var channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

            var worker = Task.Run(() =>
            {
                var pending = new List<byte>(targetBytes * 2);
                try
                {
                    var generated = _tts.GenerateWithCallbackProgress(
                        text,
                        _options.Speed,
                        _options.SpeakerId,
                        (samplesPtr, n, _) =>
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                return 0;
                            }

                            var blockBytes = FloatSamplesToPcm16Bytes(samplesPtr, n);
                            pending.AddRange(blockBytes);

                            while (pending.Count >= targetBytes)
                            {
                                var chunk = pending.GetRange(0, targetBytes).ToArray();
                                pending.RemoveRange(0, targetBytes);
                                channel.Writer.TryWrite(chunk);
                            }

                            return 1;
                        });
                    generated.Dispose();

                    if (pending.Count > 0)
                    {
                        channel.Writer.TryWrite(pending.ToArray());
                    }

                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, cancellationToken);

            await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return chunk;
            }

            await worker;
        }
        finally
        {
            _synthesisLock.Release();
        }
    }

    private static string ResolvePath(string modelDir, string fileOrDirName)
    {
        var p = Path.Combine(modelDir, fileOrDirName);
        if (File.Exists(p) || Directory.Exists(p))
        {
            return p;
        }

        throw new FileNotFoundException($"Kokoro required file/dir not found: {p}");
    }

    private static string ResolveOptionalPath(string modelDir, string rawPath)
    {
        if (Path.IsPathRooted(rawPath))
        {
            if (File.Exists(rawPath))
            {
                return rawPath;
            }

            throw new FileNotFoundException($"Kokoro lexicon not found: {rawPath}");
        }

        if (File.Exists(rawPath))
        {
            return Path.GetFullPath(rawPath);
        }

        var candidate = Path.Combine(modelDir, rawPath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException($"Kokoro lexicon not found: {candidate}");
    }

    private static string ResolveLexicon(string modelDir, string? configuredLexicon)
    {
        if (!string.IsNullOrWhiteSpace(configuredLexicon))
        {
            return ResolveOptionalPath(modelDir, configuredLexicon);
        }

        // For sherpa Kokoro multi-lang v1.0 model packs.
        var known = new[]
        {
            "lexicon-zh.txt",
            "lexicon-us-en.txt",
            "lexicon-gb-en.txt"
        };

        foreach (var name in known)
        {
            var p = Path.Combine(modelDir, name);
            if (File.Exists(p))
            {
                return p;
            }
        }

        var anyLexicon = Directory.EnumerateFiles(modelDir, "lexicon*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(anyLexicon))
        {
            return anyLexicon;
        }

        throw new FileNotFoundException($"No Kokoro lexicon found in {modelDir}");
    }

    private static byte[] FloatSamplesToPcm16Bytes(IntPtr ptr, int sampleCount)
    {
        var floats = new float[sampleCount];
        Marshal.Copy(ptr, floats, 0, sampleCount);

        var bytes = new byte[sampleCount * sizeof(short)];
        for (var i = 0; i < sampleCount; i++)
        {
            var scaled = (int)Math.Round(Math.Clamp(floats[i], -1.0f, 1.0f) * short.MaxValue);
            var pcm = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(pcm & 0xff);
            bytes[(i * 2) + 1] = (byte)((pcm >> 8) & 0xff);
        }

        return bytes;
    }

    public void Dispose()
    {
        _tts.Dispose();
        _synthesisLock.Dispose();
    }
}
